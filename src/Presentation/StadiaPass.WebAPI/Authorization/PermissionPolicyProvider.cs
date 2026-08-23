using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using StadiaPass.Application.Common.Authorization;

namespace StadiaPass.WebAPI.Authorization;

/// <summary>
/// Turns any permission string from <see cref="StadiaPassPermissions"/> into an authorization policy on
/// demand, so endpoints can call <c>RequireAuthorization(StadiaPassPermissions.Tickets.Create)</c> without
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
