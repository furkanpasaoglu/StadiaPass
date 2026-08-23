using MediatR;

namespace StadiaPass.Application.Identity.Users.Commands.CreateUser;

public sealed record CreateUserCommand(
    string Username,
    string? Email,
    string? FirstName,
    string? LastName,
    string Password,
    IReadOnlyList<string> Roles) : IRequest<UserDto>;
