using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace StadiaPass.WebMVC.Authentication;

/// <summary>
/// Keycloak issues an access token that lives half an hour, while the session it belongs to lives for hours.
/// The site replays that token on every call to the API, so without this the user keeps looking signed in
/// while everything they click starts coming back 401. The cookie is validated on each request, which is the
/// moment to trade an expiring token for a fresh one.
/// </summary>
internal sealed partial class TokenRefreshingCookieEvents(
    IOptionsMonitor<OpenIdConnectOptions> openIdConnectOptions,
    ILogger<TokenRefreshingCookieEvents> logger) : CookieAuthenticationEvents
{
    /// <summary>The name the OIDC handler stores the access token expiry under.</summary>
    public const string ExpiresAtTokenName = "expires_at";

    /// <summary>Renew before the token actually dies, so a call already in flight cannot outlive it.</summary>
    private static readonly TimeSpan RenewalWindow = TimeSpan.FromMinutes(2);

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (!IsExpiring(context.Properties))
        {
            return;
        }

        if (context.Properties.GetTokenValue(OpenIdConnectParameterNames.RefreshToken)
            is not { Length: > 0 } refreshToken)
        {
            await SignOutAsync(context);

            return;
        }

        var refreshed = await RequestTokensAsync(refreshToken, context.HttpContext.RequestAborted);

        if (refreshed is null)
        {
            // The refresh token is spent, revoked or the session ended in Keycloak: there is nothing left to
            // recover, so end the local session too rather than serving a shell of a signed-in page.
            await SignOutAsync(context);

            return;
        }

        Store(context.Properties, refreshed, refreshToken);

        context.ShouldRenew = true;
    }

    private static bool IsExpiring(AuthenticationProperties properties) =>
        properties.GetTokenValue(ExpiresAtTokenName) is { Length: > 0 } value
        && DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt)
        && DateTimeOffset.UtcNow >= expiresAt - RenewalWindow;

    private static async Task SignOutAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();

        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Keycloak returns a new refresh token on every exchange, but is not obliged to; the previous one is
    /// carried forward when it does not, otherwise the next renewal would have nothing to present.
    /// </summary>
    private static void Store(
        AuthenticationProperties properties,
        TokenResponse refreshed,
        string previousRefreshToken)
    {
        List<AuthenticationToken> tokens =
        [
            new() { Name = OpenIdConnectParameterNames.AccessToken, Value = refreshed.AccessToken },
            new()
            {
                Name = ExpiresAtTokenName,
                Value = DateTimeOffset.UtcNow
                    .AddSeconds(refreshed.ExpiresIn)
                    .ToString("o", CultureInfo.InvariantCulture)
            },
            new()
            {
                Name = OpenIdConnectParameterNames.RefreshToken,
                Value = refreshed.RefreshToken is { Length: > 0 } issued ? issued : previousRefreshToken
            }
        ];

        if (refreshed.IdToken is { Length: > 0 } idToken)
        {
            tokens.Add(new AuthenticationToken { Name = OpenIdConnectParameterNames.IdToken, Value = idToken });
        }
        else if (properties.GetTokenValue(OpenIdConnectParameterNames.IdToken) is { Length: > 0 } previousIdToken)
        {
            // Sign-out needs the id token as a hint, so it must survive a renewal that does not reissue it.
            tokens.Add(new AuthenticationToken
            {
                Name = OpenIdConnectParameterNames.IdToken,
                Value = previousIdToken
            });
        }

        properties.StoreTokens(tokens);
    }

    private async Task<TokenResponse?> RequestTokensAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var options = openIdConnectOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);

        try
        {
            var configuration = options.Configuration
                ?? await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = OpenIdConnectGrantTypes.RefreshToken,
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = options.ClientId ?? string.Empty,
                    ["client_secret"] = options.ClientSecret ?? string.Empty
                })
            };

            using var response = await options.Backchannel.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                RefreshRejected(logger, (int)response.StatusCode);

                return null;
            }

            return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            RefreshFailed(logger, exception);

            return null;
        }
    }

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Information,
        Message = "Keycloak refused to renew the session, status {StatusCode}; signing the user out")]
    private static partial void RefreshRejected(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Could not reach Keycloak to renew the session")]
    private static partial void RefreshFailed(ILogger logger, Exception exception);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
