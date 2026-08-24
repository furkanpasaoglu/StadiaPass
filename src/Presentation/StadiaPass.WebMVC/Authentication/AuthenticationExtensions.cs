using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.WebMVC.Authentication;

public static class AuthenticationExtensions
{
    /// <summary>
    /// Marks a challenge as "take me to the sign-up form". Keycloak exposes registration as a sibling of the
    /// authorization endpoint, so the redirect is retargeted there while the handler keeps ownership of
    /// state, nonce and PKCE.
    /// </summary>
    public const string RegisterItem = "stadiapass:register";

    private const string AuthorizePath = "/protocol/openid-connect/auth";

    private const string RegisterPath = "/protocol/openid-connect/registrations";

    public static IHostApplicationBuilder AddKeycloakLogin(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<KeycloakOptions>()
            .Bind(builder.Configuration.GetSection(KeycloakOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var keycloak = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
                       ?? new KeycloakOptions();

        // The ticket lives in Redis; the cookie only carries its key.
        builder.AddRedisDistributedCache("cache");
        builder.Services.AddSingleton<ITicketStore, DistributedCacheTicketStore>();
        builder.Services
            .AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<ITicketStore>((options, store) => options.SessionStore = store);

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "stadiapass.session";
                options.SlidingExpiration = true;
                options.AccessDeniedPath = "/Account/Denied";

                // The session outlives the access token it carries, so every request re-checks whether
                // that token is about to expire and renews it against Keycloak.
                options.EventsType = typeof(TokenRefreshingCookieEvents);
            })
            .AddKeycloakOpenIdConnect(
                keycloak.ServiceName,
                keycloak.Realm,
                OpenIdConnectDefaults.AuthenticationScheme,
                options =>
                {
                    options.ClientId = keycloak.ClientId;
                    options.ClientSecret = keycloak.ClientSecret;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.UsePkce = true;
                    options.SaveTokens = true;
                    options.RequireHttpsMetadata = false;
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters.NameClaimType = "preferred_username";

                    options.Scope.Clear();
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");

                    // Without this the access token carries no address, and the ticket confirmation has
                    // nowhere to go: the purchase message takes the buyer's email with it rather than
                    // making a consumer ask Keycloak who they were.
                    options.Scope.Add("email");

                    // Every abandoned sign-in leaves a correlation and a nonce cookie behind. All the local
                    // services share the "localhost" cookie jar, so without a short lifetime they pile up
                    // until the request header exceeds what Keycloak accepts and it answers 431.
                    options.CorrelationCookie.MaxAge = TimeSpan.FromMinutes(5);
                    options.NonceCookie.MaxAge = TimeSpan.FromMinutes(5);

                    options.Events.OnRedirectToIdentityProvider = context =>
                    {
                        if (context.Properties.Items.ContainsKey(RegisterItem))
                        {
                            context.ProtocolMessage.IssuerAddress = context.ProtocolMessage.IssuerAddress
                                .Replace(AuthorizePath, RegisterPath, StringComparison.Ordinal);
                        }

                        return Task.CompletedTask;
                    };

                    options.Events.OnTokenValidated = context =>
                    {
                        CompactSession(context, keycloak.ApiClientId);

                        return Task.CompletedTask;
                    };
                });

        // The MVC app resolves the same permission strings as the API, from the same shared kernel.
        builder.Services.AddStadiaPassPermissions(keycloak.ApiClientId);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<TokenBearerHandler>();
        builder.Services.AddScoped<TokenRefreshingCookieEvents>();

        return builder;
    }

    /// <summary>
    /// Roles are expanded into permission claims once at sign-in and the bulky protocol claims are dropped,
    /// so the ticket stays small and every later request reads permissions instead of re-parsing Keycloak
    /// JSON. Only the tokens the app actually needs are carried forward.
    /// </summary>
    private static void CompactSession(TokenValidatedContext context, string apiClientId)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        var permissions = KeycloakRoleReader.ReadPermissions(
            identity.FindFirst(KeycloakRoleReader.RealmAccessClaim)?.Value,
            identity.FindFirst(KeycloakRoleReader.ResourceAccessClaim)?.Value,
            apiClientId);

        string[] dropped =
        [
            KeycloakRoleReader.RealmAccessClaim,
            KeycloakRoleReader.ResourceAccessClaim,
            "allowed-origins",
            "scope",
            "acr",
            "at_hash",
            "sid",
            "aud",
            "azp",
            "iss",
            "jti",
            "typ",
            "session_state",
            "auth_time",
            "iat",
            "exp",
            "nbf"
        ];

        foreach (var claim in identity.Claims
                     .Where(claim => dropped.Contains(claim.Type, StringComparer.Ordinal))
                     .ToArray())
        {
            identity.RemoveClaim(claim);
        }

        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(StadiaPassClaimTypes.Permission, permission));
        }

        // The refresh token and the expiry stay: without them the session cannot be renewed and the user
        // would silently lose access to the API half an hour in. They cost nothing in the cookie because
        // the ticket itself lives in Redis.
        var kept = context.Properties!.GetTokens()
            .Where(token => token.Name is OpenIdConnectParameterNames.AccessToken
                                       or OpenIdConnectParameterNames.IdToken
                                       or OpenIdConnectParameterNames.RefreshToken
                                       or TokenRefreshingCookieEvents.ExpiresAtTokenName)
            .ToArray();

        context.Properties!.StoreTokens(kept);
    }
}
