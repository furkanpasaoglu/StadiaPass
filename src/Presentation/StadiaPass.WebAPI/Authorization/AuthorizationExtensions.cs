using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.SharedKernel.Authorization;

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

        builder.Services.AddStadiaPassPermissions(keycloak.Audience);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();

        return builder;
    }
}
