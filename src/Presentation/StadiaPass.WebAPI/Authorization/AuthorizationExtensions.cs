using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace StadiaPass.WebAPI.Authorization;

public static class AuthorizationExtensions
{
    public static IHostApplicationBuilder AddPermissionBasedAuthorization(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<KeycloakOptions>()
            .Bind(builder.Configuration.GetSection(KeycloakOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var keycloak = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
                       ?? new KeycloakOptions();

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddKeycloakJwtBearer(keycloak.ServiceName, keycloak.Realm, options =>
            {
                options.Audience = keycloak.Audience;
                options.RequireHttpsMetadata = false;
                options.MapInboundClaims = false;
            });

        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddSingleton<IClaimsTransformation, KeycloakPermissionClaimsTransformation>();

        return builder;
    }
}
