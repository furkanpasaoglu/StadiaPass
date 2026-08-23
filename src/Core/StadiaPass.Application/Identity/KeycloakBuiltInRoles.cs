using System.Collections.Frozen;

namespace StadiaPass.Application.Identity;

/// <summary>
/// Roles Keycloak ships with every realm. They are infrastructure, not StadiaPass business roles, so the
/// portal never lists or assigns them.
/// </summary>
internal static class KeycloakBuiltInRoles
{
    private static readonly FrozenSet<string> Names =
        new[] { "offline_access", "uma_authorization" }.ToFrozenSet(StringComparer.Ordinal);

    private const string DefaultRolesPrefix = "default-roles";

    public static bool Is(string roleName) =>
        Names.Contains(roleName) || roleName.StartsWith(DefaultRolesPrefix, StringComparison.Ordinal);
}
