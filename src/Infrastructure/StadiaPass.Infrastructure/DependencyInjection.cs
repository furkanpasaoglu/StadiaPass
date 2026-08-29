using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Identity;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Matches.Search;
using StadiaPass.Infrastructure.Caching;
using StadiaPass.Infrastructure.Email;
using StadiaPass.Infrastructure.Identity;
using StadiaPass.Infrastructure.Locking;
using StadiaPass.Infrastructure.Messaging;
using StadiaPass.Infrastructure.Payments;
using StadiaPass.Infrastructure.Search;
using StadiaPass.Infrastructure.Time;
using Stripe;

namespace StadiaPass.Infrastructure;

public static class DependencyInjection
{
    public const string CacheConnectionName = "cache";

    public const string MessagingConnectionName = "messaging";

    public const string KeycloakServiceName = "keycloak";

    public const string SearchConnectionName = "search";

    /// <summary>How long any one call to Elasticsearch may take before it is given up on.</summary>
    private static readonly TimeSpan SearchRequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Named client so the Stripe timeout is a configuration value rather than a default.</summary>
    public const string StripeHttpClientName = "stripe";

    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddRedisDistributedCache(CacheConnectionName);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        builder.Services.AddScoped<ICacheService, RedisCacheService>();

        // The multiplexer comes from the Aspire Redis component that AddRedisDistributedCache already set up,
        // so the lock shares one connection with the cache rather than opening a second.
        builder.Services.AddSingleton<IDistributedLock, RedisDistributedLock>();

        builder.Services
            .AddOptions<KeycloakAdminOptions>()
            .Bind(builder.Configuration.GetSection(KeycloakAdminOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Both the token endpoint and the Admin REST API live behind the same Keycloak base address, which
        // service discovery resolves from the Aspire resource name.
        builder.Services
            .AddHttpClient(KeycloakAdminService.HttpClientName, client =>
                client.BaseAddress = new Uri($"https+http://{KeycloakServiceName}"));

        builder.Services.AddSingleton<KeycloakAdminTokenProvider>();

        builder.Services
            .AddHttpClient<IKeycloakAdminService, KeycloakAdminService>(client =>
                client.BaseAddress = new Uri($"https+http://{KeycloakServiceName}"));

        builder.AddPayments();
        builder.AddSearch();
        builder.AddMessaging();
        builder.AddEmail();

        return builder;
    }

    /// <summary>
    /// Elasticsearch, and only for search.
    /// </summary>
    /// <remarks>
    /// The listing, the seat map and every number on them come from PostgreSQL and keep coming from
    /// PostgreSQL. What is here answers one question - which fixtures match the words a visitor typed - and
    /// answers it with an analyzer and a relevance score, which is the part a <c>LIKE</c> cannot do. The
    /// index holds no counters and is not a source of truth: it can be dropped and rebuilt from the database
    /// at any moment, and the reindex command exists to do exactly that.
    /// </remarks>
    private static void AddSearch(this IHostApplicationBuilder builder)
    {
        builder.AddElasticsearchClient(
            SearchConnectionName,
            settings =>
                // Not part of this API's readiness, deliberately. The default registration puts the cluster
                // into /health, which would have a search outage report the whole service as unable to take
                // traffic - while it is in fact still listing fixtures, holding seats and taking payments.
                // A cluster that is down is visible where it belongs: on the search resource's own health in
                // the dashboard, in the warning the query handler logs, and in the searchAvailable flag the
                // API hands back. Measured before it was written: with the check in, one stopped container
                // made /health hang until it timed out.
                settings.DisableHealthChecks = true,
            clientSettings => clientSettings
                // Without a ceiling the client spends a minute discovering that an unreachable cluster is
                // unreachable, and the request that was supposed to fall back to the plain listing hangs for
                // the whole of it. Long enough for a slow bulk write, short enough that a visitor waiting on
                // a dead cluster gets their listing instead.
                .RequestTimeout(SearchRequestTimeout));

        builder.Services.AddSingleton<SearchMetrics>();
        builder.Services.AddScoped<IMatchSearchIndex, ElasticMatchSearchIndex>();
        builder.Services.AddHostedService<SearchIndexInitializer>();
        builder.Services.AddHostedService<SearchIndexFreshnessWorker>();
    }

    /// <summary>
    /// Mail is optional on purpose. No credentials means a clone still sells tickets and says out loud that
    /// it had nowhere to send the confirmation, rather than refusing to start over a feature nobody asked
    /// it for yet - which is the opposite of how the secrets that carry money are treated.
    /// </summary>
    private static void AddEmail(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<SmtpOptions>()
            .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
            .ValidateDataAnnotations();

        builder.Services.AddTransient<IEmailService, MailKitEmailService>();
    }

    /// <summary>
    /// MassTransit over RabbitMQ, with both ends of the conversation in this one process for now. That is a
    /// perfectly ordinary way to run a system that has not been split up yet, and it is also the point: the
    /// message really does leave for the broker and really does come back, so the day a consumer moves into
    /// its own service, nothing about this side has to change.
    /// </summary>
    private static void AddMessaging(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(MessagingConnectionName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{MessagingConnectionName} is not set. RabbitMQ is orchestrated by the "
                + "Aspire AppHost, so the API is expected to be started through it.");

        builder.Services.AddMassTransit(bus =>
        {
            // Exchange and queue names come out as ticket-purchased-event rather than TicketPurchasedEvent,
            // which is what anybody looking at the RabbitMQ management page expects to see.
            bus.SetKebabCaseEndpointNameFormatter();

            bus.AddConsumer<TicketPurchasedEventConsumer>();
            bus.AddConsumer<PaymentSucceededConsumer>();
            bus.AddConsumer<PaymentDisputedConsumer>();
            bus.AddConsumer<PaymentRefundedConsumer>();
            bus.AddConsumer<RefundOwedConsumer>();
            bus.AddConsumer<MatchCatalogueChangedConsumer>();
            bus.AddConsumer<MatchCancelledConsumer>();
            bus.AddConsumer<MatchCancellationNoticeConsumer>();

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(connectionString));

                // MassTransit does not retry unless it is told to. Without this a consumer that throws once
                // is finished: the message goes straight to its error queue, which for an SMTP server that
                // blinked means a ticket confirmation nobody ever receives, and for a chargeback means a seat
                // left sold to somebody who took their money back. Every consumer here is written to be safe
                // run twice - the inbox already refused anything the provider sent twice, and the commands
                // ask the database what is true rather than assuming - so retrying costs nothing and saves
                // exactly the failures worth saving.
                //
                // Five goes over about a minute, and then the error queue, which is where a message that is
                // genuinely broken belongs: retrying it forever would only hide it.
                rabbit.UseMessageRetry(retry =>
                    retry.Exponential(
                        retryLimit: 5,
                        minInterval: TimeSpan.FromSeconds(1),
                        maxInterval: TimeSpan.FromSeconds(30),
                        intervalDelta: TimeSpan.FromSeconds(2)));

                rabbit.ConfigureEndpoints(context);
            });
        });

        builder.Services.AddScoped<IEventBus, MassTransitEventBus>();
    }

    /// <summary>
    /// Which payment provider answers is a configuration decision, not a code one: the application layer
    /// only ever sees <see cref="IPaymentService"/>. A clone with no configuration gets the mock, so the
    /// checkout works end to end - decline path included - without a Stripe account.
    /// </summary>
    private static void AddPayments(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<PaymentOptions>()
            .Bind(builder.Configuration.GetSection(PaymentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSingleton<IValidateOptions<PaymentOptions>, PaymentOptionsValidator>();

        // Registered whichever provider is chosen. An endpoint that quietly stops existing when somebody
        // flips a config value is a worse surprise than one that answers and refuses.
        builder.Services.AddScoped<IPaymentWebhookReader, StripeWebhookReader>();

        var payments = builder.Configuration.GetSection(PaymentOptions.SectionName).Get<PaymentOptions>()
                       ?? new PaymentOptions();

        switch (payments.Type)
        {
            case PaymentProviderType.Stripe:
                builder.Services
                    .AddHttpClient(StripeHttpClientName)
                    .ConfigureHttpClient(client =>
                        client.Timeout = TimeSpan.FromSeconds(payments.TimeoutSeconds));

                // Stripe retries on its own and replays the idempotency key while doing so, which is what
                // makes a retry safe on an endpoint that moves money.
                builder.Services.AddSingleton<IStripeClient>(provider => new StripeClient(
                    payments.SecretKey,
                    httpClient: new SystemNetHttpClient(
                        provider.GetRequiredService<IHttpClientFactory>().CreateClient(StripeHttpClientName),
                        maxNetworkRetries: StripePaymentService.MaxNetworkRetries,
                        enableTelemetry: false)));

                builder.Services.AddScoped<IPaymentService, StripePaymentService>();

                break;

            case PaymentProviderType.Mock:
            default:
                builder.Services.AddScoped<IPaymentService, MockPaymentService>();

                break;
        }
    }
}
