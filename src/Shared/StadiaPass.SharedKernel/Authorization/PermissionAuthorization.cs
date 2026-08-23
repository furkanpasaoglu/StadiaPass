using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace StadiaPass.SharedKernel.Authorization;

public sealed class PermissionAuthorizationOptions
{
    /// <summary>Client id whose <c>resource_access</c> entry is inspected alongside the realm roles.</summary>
    public string ClientId { get; set; } = string.Empty;
}

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;

    public override string ToString() => $"Permission: {Permission}";
}

internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Turns any permission string from <see cref="StadiaPassPermissions"/> into an authorization policy on
/// demand, so a caller can write <c>RequireAuthorization(StadiaPassPermissions.Tickets.Purchase)</c> without
/// a matching <c>AddPolicy</c> registration ever being written by hand.
/// </summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackProvider = new(options);

    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies = new(StringComparer.Ordinal);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallbackProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallbackProvider.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        StadiaPassPermissions.IsDefined(policyName)
            ? Task.FromResult<AuthorizationPolicy?>(_policies.GetOrAdd(policyName, BuildPolicy))
            : _fallbackProvider.GetPolicyAsync(policyName);

    private static AuthorizationPolicy BuildPolicy(string permission) =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
}

/// <summary>
/// Keycloak ships roles inside the <c>realm_access</c> and <c>resource_access</c> JSON claims. Those role
/// names ARE the permission strings, so they are flattened into individual <c>permission</c> claims and
/// filtered against <see cref="StadiaPassPermissions"/> - anything the application does not declare is
/// discarded rather than silently trusted.
/// </summary>
internal sealed class KeycloakPermissionClaimsTransformation(IOptions<PermissionAuthorizationOptions> options)
    : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity
            || identity.HasClaim(claim => claim.Type is StadiaPassClaimTypes.Permission))
        {
            return Task.FromResult(principal);
        }

        var permissions = KeycloakRoleReader.ReadPermissions(
            principal.FindFirst(KeycloakRoleReader.RealmAccessClaim)?.Value,
            principal.FindFirst(KeycloakRoleReader.ResourceAccessClaim)?.Value,
            options.Value.ClientId);

        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(StadiaPassClaimTypes.Permission, permission));
        }

        return Task.FromResult(principal);
    }
}

public static class PermissionAuthorizationExtensions
{
    /// <summary>
    /// Wires permission-based authorization: dynamic policies, the permission handler and the Keycloak role
    /// to permission claims transformation. Shared by the API and the MVC front end so both agree on what a
    /// permission means.
    /// </summary>
    public static IServiceCollection AddStadiaPassPermissions(this IServiceCollection services, string clientId)
    {
        services.Configure<PermissionAuthorizationOptions>(options => options.ClientId = clientId);

        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IClaimsTransformation, KeycloakPermissionClaimsTransformation>();

        return services;
    }

    /// <summary>Usable from views to hide actions the signed-in user is not allowed to perform.</summary>
    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.HasClaim(StadiaPassClaimTypes.Permission, permission);
}
