using System.Globalization;
using Serilog;
using StadiaPass.McpServer.Api;
using StadiaPass.McpServer.Tools;
using StadiaPass.ServiceDefaults.Configuration;
using StadiaPass.ServiceDefaults.Logging;

// The same bootstrap-logger window the API covers: a failure while the container is being built should
// leave a line behind rather than a silent exit. AddServiceDefaults swaps in the configured logger.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddVaultConfiguration();
    builder.AddServiceDefaults();

    builder.Services
        .AddOptions<ServiceAccountOptions>()
        .Bind(builder.Configuration.GetSection(ServiceAccountOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddSingleton(TimeProvider.System);

    builder.Services.AddHttpClient(nameof(ServiceAccountTokenHandler));
    builder.Services.AddTransient<ServiceAccountTokenHandler>();

       builder.Services.AddHttpClient<ICatalogueApiClient, CatalogueApiClient>(client =>
            client.BaseAddress = new Uri("https+http://webapi"))
        .AddHttpMessageHandler<ServiceAccountTokenHandler>();

    var serviceAccount = builder.Configuration
        .GetSection(ServiceAccountOptions.SectionName)
        .Get<ServiceAccountOptions>() ?? new ServiceAccountOptions();

    var tools = builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithTools<CatalogueTools>();

    if (serviceAccount.IsConfigured)
    {
        tools.WithTools<AnalyticsTools>();
    }
    else
    {
        Log.Warning(
            "No service-account secret configured; the analytics tool is not being offered. The public "
            + "catalogue tools are unaffected.");
    }

    builder.Services.AddProblemDetails();

    var app = builder.Build();

    app.UseStadiaPassRequestLogging();
    app.UseExceptionHandler();

    app.MapDefaultEndpoints();
    app.MapMcp("/mcp");

    await app.RunAsync();
}
catch (Exception exception)
{
    Log.Fatal(exception, "StadiaPass MCP server terminated unexpectedly");

    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
