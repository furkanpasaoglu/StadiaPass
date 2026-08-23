using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using StadiaPass.Application.Common.Abstractions;

namespace StadiaPass.Infrastructure.Caching;

internal sealed class RedisCacheService(IDistributedCache cache) : ICacheService
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var payload = await cache.GetStringAsync(key, cancellationToken);

        return payload is null ? default : JsonSerializer.Deserialize<T>(payload, SerializerOptions);
    }

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) =>
        cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(value, SerializerOptions),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration },
            cancellationToken);

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(key, cancellationToken);
}
