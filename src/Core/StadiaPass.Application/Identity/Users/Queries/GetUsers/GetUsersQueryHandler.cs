using MediatR;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.Identity.Users.Queries.GetUsers;

internal sealed class GetUsersQueryHandler(IKeycloakAdminService keycloak)
    : IRequestHandler<GetUsersQuery, UserListDto>
{
    public async Task<UserListDto> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var first = (Math.Max(request.Page, 1) - 1) * pageSize;

        var usersTask = keycloak.GetUsersAsync(request.Search, first, pageSize, cancellationToken);
        var rolesTask = keycloak.GetRealmRolesAsync(cancellationToken);

        await Task.WhenAll(usersTask, rolesTask);

        var users = await usersTask;
        var assignable = (await rolesTask)
            .Select(role => role.Name)
            .Where(name => !StadiaPassPermissions.IsPermissionRole(name) && !KeycloakBuiltInRoles.Is(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Role mappings are per-user calls in Keycloak; fan them out instead of walking the page serially.
        var detailed = await Task.WhenAll(users.Select(async user =>
        {
            var roles = await keycloak.GetUserRealmRolesAsync(user.Id, cancellationToken);

            return new UserDto(
                user.Id,
                user.Username,
                user.Email,
                user.FirstName,
                user.LastName,
                user.Enabled,
                [.. roles
                    .Select(role => role.Name)
                    .Where(name => !KeycloakBuiltInRoles.Is(name))
                    .Order(StringComparer.Ordinal)]);
        }));

        return new UserListDto(detailed, assignable);
    }
}
