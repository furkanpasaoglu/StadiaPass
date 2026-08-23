using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Identity.Users.Commands.CreateUser;

namespace StadiaPass.Application.Identity.Users.Commands.UpdateUserRoles;

internal sealed class UpdateUserRolesCommandHandler(IKeycloakAdminService keycloak)
    : IRequestHandler<UpdateUserRolesCommand, UserDto>
{
    public async Task<UserDto> Handle(UpdateUserRolesCommand request, CancellationToken cancellationToken)
    {
        var user = await keycloak.GetUserAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        var current = (await keycloak.GetUserRealmRolesAsync(user.Id, cancellationToken))
            .Where(role => !KeycloakBuiltInRoles.Is(role.Name))
            .ToArray();

        var desired = await RoleAssignment.ResolveAsync(keycloak, request.Roles, cancellationToken);
        var desiredNames = desired.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);
        var currentNames = current.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);

        var toAdd = desired.Where(role => !currentNames.Contains(role.Name)).ToArray();
        var toRemove = current.Where(role => !desiredNames.Contains(role.Name)).ToArray();

        if (toAdd.Length is not 0)
        {
            await keycloak.AssignUserRealmRolesAsync(user.Id, toAdd, cancellationToken);
        }

        if (toRemove.Length is not 0)
        {
            await keycloak.RemoveUserRealmRolesAsync(user.Id, toRemove, cancellationToken);
        }

        return new UserDto(
            user.Id,
            user.Username,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Enabled,
            [.. desiredNames.Order(StringComparer.Ordinal)]);
    }
}
