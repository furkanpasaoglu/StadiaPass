using MediatR;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.Identity.Roles.Queries.GetRoles;

internal sealed class GetRolesQueryHandler(IKeycloakAdminService keycloak)
    : IRequestHandler<GetRolesQuery, RoleListDto>
{
    public async Task<RoleListDto> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        var realmRoles = await keycloak.GetRealmRolesAsync(cancellationToken);

        // Permission roles are the building blocks; only the composite business roles are shown as "roles".
        var businessRoles = realmRoles
            .Where(role => !StadiaPassPermissions.IsPermissionRole(role.Name) && !KeycloakBuiltInRoles.Is(role.Name))
            .OrderBy(role => role.Name, StringComparer.Ordinal)
            .ToArray();

        var permissionLookup = await Task.WhenAll(businessRoles.Select(async role =>
        {
            var composites = role.Composite
                ? await keycloak.GetRoleCompositesAsync(role.Id, cancellationToken)
                : [];

            return new RoleDto(
                role.Id,
                role.Name,
                role.Description,
                [.. composites
                    .Select(composite => composite.Name)
                    .Where(StadiaPassPermissions.IsPermissionRole)
                    .Order(StringComparer.Ordinal)]);
        }));

        return new RoleListDto(
            permissionLookup,
            [.. StadiaPassPermissions.Groups.Select(group =>
                new PermissionGroupDto(group.Name, group.Permissions))]);
    }
}
