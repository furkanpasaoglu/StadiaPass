using MediatR;
using StadiaPass.Application.Common.Exceptions;

namespace StadiaPass.Application.Identity.Users.Commands.UpdateUser;

internal sealed class UpdateUserCommandHandler(IKeycloakAdminService keycloak)
    : IRequestHandler<UpdateUserCommand, UserDto>
{
    public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        _ = await keycloak.GetUserAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        await keycloak.UpdateUserAsync(
            request.UserId,
            request.Email,
            request.FirstName,
            request.LastName,
            request.Enabled,
            cancellationToken);

        var updated = await keycloak.GetUserAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        var roles = await keycloak.GetUserRealmRolesAsync(updated.Id, cancellationToken);

        return new UserDto(
            updated.Id,
            updated.Username,
            updated.Email,
            updated.FirstName,
            updated.LastName,
            updated.Enabled,
            [.. roles
                .Select(role => role.Name)
                .Where(name => !KeycloakBuiltInRoles.Is(name))
                .Order(StringComparer.Ordinal)]);
    }
}
