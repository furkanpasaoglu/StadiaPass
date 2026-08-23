using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.Identity.Roles.Commands.DeleteRole;

internal sealed class DeleteRoleCommandHandler(IKeycloakAdminService keycloak)
    : IRequestHandler<DeleteRoleCommand>
{
    public async Task Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        if (StadiaPassPermissions.IsPermissionRole(request.RoleName))
        {
            throw new ConflictException("A permission role is part of the application and cannot be deleted.");
        }

        _ = await keycloak.FindRealmRoleAsync(request.RoleName, cancellationToken)
            ?? throw new NotFoundException("Role", request.RoleName);

        await keycloak.DeleteRealmRoleAsync(request.RoleName, cancellationToken);
    }
}
