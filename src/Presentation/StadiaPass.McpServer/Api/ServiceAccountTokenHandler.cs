using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace StadiaPass.McpServer.Api;

/// <summary>
/// Puts the server's own bearer token on every call to the API, fetched with the client-credentials grant
/// and kept until shortly before it expires.
/// </summary>
/// <remarks>
/// <para>
/// A handler rather than a call at the top of each method, so no future tool can be written that forgets
/// it. When no secret is configured the handler is a pass-through and the anonymous catalogue tools keep
/// working exactly as they did - an identity this server has not been given is not a reason for browsing
/// to stop.
/// </para>
/// <para>
/// The token is cached with a minute of headroom. Asking Keycloak for a fresh one on every tool call would
/// put a round trip in front of every question an analyst asks, and a token that expires between the check
/// and the request is worse than a token replaced a minute early.
/// </para>
/// </remarks>
internal sealed partial class ServiceAccountTokenHandler(
    IHttpClientFactory httpClientFactory,
    IOptions<ServiceAccountOptions> options,
    TimeProvider timeProvider,
    ILogger<ServiceAccountTokenHandler> logger) : DelegatingHandler
{
    /// <summary>Renew this far before expiry, so a token never dies in flight.</summary>
    private static readonly TimeSpan Headroom = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly ServiceAccountOptions _options = options.Value;

    private string? _token;

    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_options.IsConfigured)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", await GetTokenAsync(cancellationToken));
        }

        return await base.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsUsable())
        {
            return _token!;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Somebody else may have renewed it while this call waited for the gate.
            if (IsUsable())
            {
                return _token!;
            }

            using var client = httpClientFactory.CreateClient(nameof(ServiceAccountTokenHandler));

            using var response = await client.PostAsync(
                _options.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _options.McpClientId,
                    ["client_secret"] = _options.McpClientSecret
                }),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Keycloak returned an empty token response.");

            _token = token.AccessToken;
            _expiresAtUtc = timeProvider.GetUtcNow().AddSeconds(token.ExpiresInSeconds);

            TokenRenewed(logger, _options.McpClientId, token.ExpiresInSeconds);

            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsUsable() => _token is not null && timeProvider.GetUtcNow() < _expiresAtUtc - Headroom;

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Service account token for {ClientId} renewed, valid for {Seconds}s")]
    private static partial void TokenRenewed(ILogger logger, string clientId, int seconds);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresInSeconds);
}
