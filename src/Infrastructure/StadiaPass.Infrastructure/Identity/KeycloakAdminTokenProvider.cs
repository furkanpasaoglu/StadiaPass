using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace StadiaPass.Infrastructure.Identity;

/// <summary>
/// Fetches and caches the service account access token used for Admin REST calls. A single token is shared
/// by every request and refreshed slightly before it expires, so an admin screen does not pay for a token
/// round trip on each call.
/// </summary>
internal sealed class KeycloakAdminTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakAdminOptions> options,
    TimeProvider timeProvider) : IDisposable
{
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;

    private DateTimeOffset _expiresAt;

    public async ValueTask<string> GetAsync(CancellationToken cancellationToken)
    {
        if (_token is { Length: > 0 } cached && timeProvider.GetUtcNow() < _expiresAt)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_token is { Length: > 0 } current && timeProvider.GetUtcNow() < _expiresAt)
            {
                return current;
            }

            var settings = options.Value;
            using var client = httpClientFactory.CreateClient(KeycloakAdminService.HttpClientName);

            using var response = await client.PostAsync(
                new Uri($"/realms/{settings.Realm}/protocol/openid-connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = settings.AdminClientId,
                    ["client_secret"] = settings.AdminClientSecret
                }),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Keycloak returned an empty token response.");

            _token = payload.AccessToken;
            _expiresAt = timeProvider.GetUtcNow().AddSeconds(payload.ExpiresIn) - ExpiryMargin;

            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
