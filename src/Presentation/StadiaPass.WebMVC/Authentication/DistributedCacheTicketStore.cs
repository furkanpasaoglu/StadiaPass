using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;

namespace StadiaPass.WebMVC.Authentication;

/// <summary>
/// Keeps the authentication ticket in Redis and leaves only a short session key in the cookie.
/// An administrator carries many roles, so the ticket - which also holds the access token replayed to the
/// API - grows past what a cookie can reliably round-trip. Server-side storage removes that ceiling and
/// makes sign-out revoke the session for real instead of only clearing the browser.
/// </summary>
internal sealed class DistributedCacheTicketStore(IDistributedCache cache) : ITicketStore
{
    private const string KeyPrefix = "stadiapass:auth:";

    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromHours(8);

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = KeyPrefix + Guid.CreateVersion7().ToString("N");

        await RenewAsync(key, ticket);

        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var options = new DistributedCacheEntryOptions();

        if (ticket.Properties.ExpiresUtc is { } expiresUtc)
        {
            options.SetAbsoluteExpiration(expiresUtc);
        }
        else
        {
            options.SetSlidingExpiration(SlidingExpiration);
        }

        return cache.SetAsync(key, TicketSerializer.Default.Serialize(ticket), options);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var payload = await cache.GetAsync(key);

        return payload is null ? null : TicketSerializer.Default.Deserialize(payload);
    }

    public Task RemoveAsync(string key) => cache.RemoveAsync(key);
}
