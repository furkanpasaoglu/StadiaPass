using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Infrastructure.Caching;
using StadiaPass.Infrastructure.Time;

namespace StadiaPass.Infrastructure;

public static class DependencyInjection
{
    public const string CacheConnectionName = "cache";

    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddRedisDistributedCache(CacheConnectionName);

        builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        builder.Services.AddScoped<ICacheService, RedisCacheService>();

        return builder;
    }
}
