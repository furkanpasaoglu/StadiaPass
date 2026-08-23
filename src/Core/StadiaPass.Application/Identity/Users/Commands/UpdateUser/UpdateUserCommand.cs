using MediatR;

namespace StadiaPass.Application.Identity.Users.Commands.UpdateUser;

public sealed record UpdateUserCommand(
    string UserId,
    string? Email,
    string? FirstName,
    string? LastName,
    bool Enabled) : IRequest<UserDto>;
