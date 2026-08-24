using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace StadiaPass.ServiceDefaults.Logging;

public static class SerilogExtensions
{
    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {ApplicationName} {CorrelationId} {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Serilog becomes the only logging provider for the process. It writes to the console for a human and,
    /// through the OpenTelemetry sink, to whatever OTLP collector the environment points at - the Aspire
    /// dashboard while developing.
    /// </summary>
    public static TBuilder AddStadiaPassSerilog<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddSerilog((services, logger) =>
        {
            var applicationName = builder.Environment.ApplicationName;

            logger
                .MinimumLevel.Information()
                // The framework narrates every request at Information; UseSerilogRequestLogging replaces that
                // with one line per request, so the raw chatter is turned down rather than duplicated.
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                // The resilience pipeline narrates every outbound call to Keycloak; one line per attempt
                // buries the request it belongs to.
                .MinimumLevel.Override("Polly", LogEventLevel.Warning)
                // The multiplexer describes its topology in forty lines every time a process starts.
                .MinimumLevel.Override("StackExchange.Redis", LogEventLevel.Warning)
                .MinimumLevel.Override("StadiaPass", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("ApplicationName", applicationName)
                .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
                .Enrich.With(new RequestContextEnricher(
                    services.GetRequiredService<IHttpContextAccessor>()))
                .Destructure.With<SensitiveDataDestructuringPolicy>()
                .WriteTo.Console(outputTemplate: ConsoleTemplate, formatProvider: CultureInfo.InvariantCulture);

            AddOpenTelemetrySink(builder, logger, applicationName);
        });

        return builder;
    }

    /// <summary>
    /// One line per request with its duration and status code, replacing the framework's three. Endpoints
    /// that are polled by machines - health probes and the Prometheus scrape - are dropped to Verbose so they
    /// never reach the sinks: at a five second scrape interval they would otherwise dominate the log.
    /// </summary>
    public static WebApplication UseStadiaPassRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

            options.GetLevel = static (httpContext, elapsed, exception) => exception is not null
                ? LogEventLevel.Error
                : httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogEventLevel.Error
                    : IsMachinePolled(httpContext.Request.Path)
                        ? LogEventLevel.Verbose
                        : LogEventLevel.Information;

            options.EnrichDiagnosticContext = static (diagnostic, httpContext) =>
            {
                diagnostic.Set("RequestHost", httpContext.Request.Host.Value);
                diagnostic.Set("RequestScheme", httpContext.Request.Scheme);

                if (httpContext.GetEndpoint()?.DisplayName is { } endpointName)
                {
                    diagnostic.Set("Endpoint", endpointName);
                }
            };
        });

        return app;
    }

    private static bool IsMachinePolled(PathString path) =>
        path.StartsWithSegments("/health")
        || path.StartsWithSegments("/alive")
        || path.StartsWithSegments("/metrics");

    private static void AddOpenTelemetrySink<TBuilder>(
        TBuilder builder,
        LoggerConfiguration logger,
        string applicationName)
        where TBuilder : IHostApplicationBuilder
    {
        var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        logger.WriteTo.OpenTelemetry(options =>
        {
            options.Endpoint = endpoint;

            // Durations are rendered into the message text by the sink. Without this the collector receives
            // "185,2638 ms" on a Turkish machine and "185.2638 ms" elsewhere.
            options.FormatProvider = CultureInfo.InvariantCulture;
            options.Protocol = string.Equals(
                builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"], "http/protobuf", StringComparison.Ordinal)
                ? OtlpProtocol.HttpProtobuf
                : OtlpProtocol.Grpc;

            options.ResourceAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["service.name"] = applicationName,
                ["deployment.environment.name"] = builder.Environment.EnvironmentName
            };

            foreach (var (key, value) in ParseHeaders(builder.Configuration["OTEL_EXPORTER_OTLP_HEADERS"]))
            {
                options.Headers[key] = value;
            }
        });
    }

    /// <summary>Aspire hands the dashboard's API key through OTEL_EXPORTER_OTLP_HEADERS as "key=value" pairs.</summary>
    private static IEnumerable<KeyValuePair<string, string>> ParseHeaders(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers))
        {
            yield break;
        }

        foreach (var pair in headers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator > 0)
            {
                yield return new KeyValuePair<string, string>(
                    pair[..separator].Trim(),
                    pair[(separator + 1)..].Trim());
            }
        }
    }
}
