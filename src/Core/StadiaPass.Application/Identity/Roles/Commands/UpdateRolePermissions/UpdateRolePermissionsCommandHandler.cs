using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Identity.Roles.Commands.CreateRoleWithPermissions;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.Identity.Roles.Commands.UpdateRolePermissions;

internal sealed class UpdateRolePermissionsCommandHandler(IKeycloakAdminService keycloak)
    : IRequestHandler<UpdateRolePermissionsCommand, RoleDto>
{
    public async Task<RoleDto> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var role = await keycloak.FindRealmRoleAsync(request.RoleName, cancellationToken)
            ?? throw new NotFoundException("Role", request.RoleName);

        var current = role.Composite
            ? await keycloak.GetRoleCompositesAsync(role.Id, cancellationToken)
            : [];

        var currentPermissions = current
            .Where(composite => StadiaPassPermissions.IsPermissionRole(composite.Name))
            .ToDictionary(composite => composite.Name, StringComparer.Ordinal);

        var desired = await PermissionRoleResolver.ResolveAsync(keycloak, request.Permissions, cancellationToken);
        var desiredNames = desired.Select(permission => permission.Name).ToHashSet(StringComparer.Ordinal);

        var toAdd = desired.Where(permission => !currentPermissions.ContainsKey(permission.Name)).ToArray();
        var toRemove = currentPermissions.Values.Where(permission => !desiredNames.Contains(permission.Name)).ToArray();

        if (toAdd.Length is not 0)
        {
            await keycloak.AddRoleCompositesAsync(role.Id, toAdd, cancellationToken);
        }

        if (toRemove.Length is not 0)
        {
            await keycloak.RemoveRoleCompositesAsync(role.Id, toRemove, cancellationToken);
        }

        return new RoleDto(role.Id, role.Name, role.Description, [.. desiredNames.Order(StringComparer.Ordinal)]);
    }
}
