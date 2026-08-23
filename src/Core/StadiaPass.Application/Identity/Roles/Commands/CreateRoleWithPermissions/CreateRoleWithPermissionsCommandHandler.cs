using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.Identity.Roles.Commands.CreateRoleWithPermissions;

internal sealed class CreateRoleWithPermissionsCommandHandler(IKeycloakAdminService keycloak)
    : IRequestHandler<CreateRoleWithPermissionsCommand, RoleDto>
{
    public async Task<RoleDto> Handle(
        CreateRoleWithPermissionsCommand request,
        CancellationToken cancellationToken)
    {
        if (await keycloak.FindRealmRoleAsync(request.Name, cancellationToken) is not null)
        {
            throw new ConflictException($"Role '{request.Name}' already exists.");
        }

        var role = await keycloak.CreateRealmRoleAsync(request.Name, request.Description, cancellationToken);

        var permissions = await PermissionRoleResolver.ResolveAsync(
            keycloak, request.Permissions, cancellationToken);

        if (permissions.Count is not 0)
        {
            await keycloak.AddRoleCompositesAsync(role.Id, permissions, cancellationToken);
        }

        return new RoleDto(
            role.Id,
            role.Name,
            role.Description,
            [.. permissions.Select(permission => permission.Name).Order(StringComparer.Ordinal)]);
    }
}

/// <summary>
/// Maps permission strings onto the realm roles that carry them, creating any that the realm does not have
/// yet. That keeps a freshly added <see cref="StadiaPassPermissions"/> constant usable without touching
/// Keycloak by hand.
/// </summary>
internal static class PermissionRoleResolver
{
    public static async Task<IReadOnlyList<KeycloakRole>> ResolveAsync(
        IKeycloakAdminService keycloak,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        var known = permissions.Where(StadiaPassPermissions.IsDefined).Distinct(StringComparer.Ordinal).ToArray();

        if (known.Length is 0)
        {
            return [];
        }

        var existing = (await keycloak.GetRealmRolesAsync(cancellationToken))
            .ToDictionary(role => role.Name, StringComparer.Ordinal);

        var resolved = new List<KeycloakRole>(known.Length);

        foreach (var permission in known)
        {
            resolved.Add(existing.TryGetValue(permission, out var role)
                ? role
                : await keycloak.CreateRealmRoleAsync(permission, "StadiaPass permission", cancellationToken));
        }

        return resolved;
    }
}
