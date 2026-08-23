using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Identity;
using StadiaPass.Infrastructure.Caching;
using StadiaPass.Infrastructure.Identity;
using StadiaPass.Infrastructure.Time;

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

        return builder;
    }
}
