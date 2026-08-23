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

        return builder;
    }

    /// <summary>
    /// The auth ticket is round-tripped in a cookie, so it has to stay small. Roles are expanded into
    /// permission claims once at sign-in, the bulky protocol claims are dropped, and only the tokens the app
    /// actually replays are stored. Without this an administrator with many roles produces a ticket that
    /// needs more chunks than the browser will carry back.
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

        var kept = context.Properties!.GetTokens()
            .Where(token => token.Name is OpenIdConnectParameterNames.AccessToken
                                       or OpenIdConnectParameterNames.IdToken)
            .ToArray();

        context.Properties!.StoreTokens(kept);
    }
}
