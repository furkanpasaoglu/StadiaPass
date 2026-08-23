using MediatR;
using StadiaPass.Application.Common.Exceptions;

namespace StadiaPass.Application.Identity.Users.Commands.CreateUser;

internal sealed class CreateUserCommandHandler(IKeycloakAdminService keycloak)
    : IRequestHandler<CreateUserCommand, UserDto>
{
    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var userId = await keycloak.CreateUserAsync(
            new NewKeycloakUser(
                request.Username,
                request.Email,
                request.FirstName,
                request.LastName,
                request.Password),
            cancellationToken);

        var assigned = await RoleAssignment.ResolveAsync(keycloak, request.Roles, cancellationToken);

        if (assigned.Count is not 0)
        {
            await keycloak.AssignUserRealmRolesAsync(userId, assigned, cancellationToken);
        }

        var user = await keycloak.GetUserAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        return new UserDto(
            user.Id,
            user.Username,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Enabled,
            [.. assigned.Select(role => role.Name).Order(StringComparer.Ordinal)]);
    }
}

internal static class RoleAssignment
{
    public static async Task<IReadOnlyList<KeycloakRole>> ResolveAsync(
        IKeycloakAdminService keycloak,
        IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken)
    {
        if (roleNames.Count is 0)
        {
            return [];
        }

        var wanted = roleNames.ToHashSet(StringComparer.Ordinal);

        return [.. (await keycloak.GetRealmRolesAsync(cancellationToken)).Where(role => wanted.Contains(role.Name))];
    }
}
