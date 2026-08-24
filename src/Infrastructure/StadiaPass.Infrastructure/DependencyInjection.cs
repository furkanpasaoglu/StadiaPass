using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Identity;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Infrastructure.Caching;
using StadiaPass.Infrastructure.Email;
using StadiaPass.Infrastructure.Identity;
using StadiaPass.Infrastructure.Locking;
using StadiaPass.Infrastructure.Messaging;
using StadiaPass.Infrastructure.Payments;
using StadiaPass.Infrastructure.Time;
using Stripe;

namespace StadiaPass.Infrastructure;

public static class DependencyInjection
{
    public const string CacheConnectionName = "cache";

    public const string MessagingConnectionName = "messaging";

    public const string KeycloakServiceName = "keycloak";

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
        builder.AddMessaging();
        builder.AddEmail();

        return builder;
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

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(connectionString));
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
                        maxNetworkRetries: 2,
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
