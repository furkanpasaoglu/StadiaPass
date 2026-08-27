using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.Identity;

/// <summary>
/// What may be handed to a person, as opposed to what may be handed to a role.
/// </summary>
/// <remarks>
/// <para>
/// The identity model has two layers on purpose: permissions are the building blocks, business roles are
/// composites of them, and a person is given a business role. Every read path already says so - the role
/// portal lists composites only, and refuses to edit a permission role as though it were one - but the write
/// path that assigns roles to a <em>user</em> took whatever name it was handed and looked it up. Anything in
/// the realm was fair game: a permission role straight onto a person, bypassing the layer that exists to
/// make who-can-do-what readable, or one of Keycloak's own infrastructure roles.
/// </para>
/// <para>
/// The realm being the only place role names live is exactly why this has to be checked rather than trusted.
/// </para>
/// </remarks>
internal static class BusinessRoles
{
    /// <summary>True when a role is one a person is meant to hold.</summary>
    public static bool IsAssignableToUser(string roleName) =>
        !string.IsNullOrWhiteSpace(roleName)
        && !StadiaPassPermissions.IsPermissionRole(roleName)
        && !KeycloakBuiltInRoles.Is(roleName);

    /// <summary>Why a name was refused, in the words the person submitting it needs to hear.</summary>
    public const string RefusalMessage =
        "'{PropertyValue}' cannot be given to a person. Permissions are granted through a business role, "
        + "and Keycloak's own roles are not ours to hand out.";
}
