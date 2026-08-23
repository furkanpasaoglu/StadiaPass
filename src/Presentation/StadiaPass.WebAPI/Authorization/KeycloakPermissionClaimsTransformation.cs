using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using StadiaPass.Application.Common.Authorization;

namespace StadiaPass.WebAPI.Authorization;

/// <summary>
/// Keycloak ships roles inside the <c>realm_access</c> and <c>resource_access</c> JSON claims. Those role
/// names ARE the permission strings, so they are flattened into individual <c>permission</c> claims and
/// filtered against <see cref="StadiaPassPermissions"/> - anything the application does not know about is
/// discarded rather than silently trusted.
/// </summary>
internal sealed class KeycloakPermissionClaimsTransformation(IOptions<KeycloakOptions> options)
    : IClaimsTransformation
{
    private const string RealmAccessClaim = "realm_access";
    private const string ResourceAccessClaim = "resource_access";
    private const string RolesProperty = "roles";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity
            || identity.HasClaim(claim => claim.Type is StadiaPassClaimTypes.Permission))
        {
            return Task.FromResult(principal);
        }

        foreach (var permission in ReadGrantedPermissions(principal, options.Value.Audience))
        {
            identity.AddClaim(new Claim(StadiaPassClaimTypes.Permission, permission));
        }

        return Task.FromResult(principal);
    }

    private static IEnumerable<string> ReadGrantedPermissions(ClaimsPrincipal principal, string audience) =>
        ReadRealmRoles(principal)
            .Concat(ReadClientRoles(principal, audience))
            .Where(StadiaPassPermissions.IsDefined)
            .Distinct(StringComparer.Ordinal);

    private static List<string> ReadRealmRoles(ClaimsPrincipal principal) =>
        principal.FindFirst(RealmAccessClaim) is { Value: var json }
            ? ReadRoles(json)
            : [];

    private static List<string> ReadClientRoles(ClaimsPrincipal principal, string audience)
    {
        if (principal.FindFirst(ResourceAccessClaim) is not { Value: var json })
        {
            return [];
        }

        using var document = TryParse(json);

        return document?.RootElement.TryGetProperty(audience, out var client) is true
            ? ReadRoles(client)
            : [];
    }

    private static List<string> ReadRoles(string json)
    {
        using var document = TryParse(json);

        return document is null ? [] : ReadRoles(document.RootElement);
    }

    private static List<string> ReadRoles(JsonElement element) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(RolesProperty, out var roles)
        && roles.ValueKind is JsonValueKind.Array
            ? [.. roles.EnumerateArray()
                .Where(role => role.ValueKind is JsonValueKind.String)
                .Select(role => role.GetString()!)]
            : [];

    private static JsonDocument? TryParse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
