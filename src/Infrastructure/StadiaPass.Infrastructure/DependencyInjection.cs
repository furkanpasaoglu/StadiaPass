using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Identity;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Infrastructure.Caching;
using StadiaPass.Infrastructure.Identity;
using StadiaPass.Infrastructure.Payments;
using StadiaPass.Infrastructure.Time;
using Stripe;

namespace StadiaPass.Infrastructure;

public static class DependencyInjection
{
    public const string CacheConnectionName = "cache";

    public const string KeycloakServiceName = "keycloak";

    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddRedisDistributedCache(CacheConnectionName);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        builder.Services.AddScoped<ICacheService, RedisCacheService>();

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

        return builder;
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

        var payments = builder.Configuration.GetSection(PaymentOptions.SectionName).Get<PaymentOptions>()
                       ?? new PaymentOptions();

        switch (payments.Type)
        {
            case PaymentProviderType.Stripe:
                builder.Services.AddSingleton<IStripeClient>(_ => new StripeClient(
                    payments.SecretKey,
                    httpClient: new SystemNetHttpClient(
                        httpClient: null,
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
