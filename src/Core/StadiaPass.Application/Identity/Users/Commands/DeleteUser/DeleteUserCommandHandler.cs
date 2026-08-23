using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;

namespace StadiaPass.Application.Identity.Users.Commands.DeleteUser;

internal sealed class DeleteUserCommandHandler(IKeycloakAdminService keycloak, ICurrentUser currentUser)
    : IRequestHandler<DeleteUserCommand>
{
    public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.UserId, currentUser.Reference, StringComparison.Ordinal))
        {
            throw new ConflictException("You cannot delete the account you are signed in with.");
        }

        _ = await keycloak.GetUserAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        await keycloak.DeleteUserAsync(request.UserId, cancellationToken);
    }
}
