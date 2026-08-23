using MediatR;

namespace StadiaPass.Application.Identity.Users.Commands.DeleteUser;

public sealed record DeleteUserCommand(string UserId) : IRequest;
