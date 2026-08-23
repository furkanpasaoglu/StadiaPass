using MediatR;

namespace StadiaPass.Application.Identity.Users.Commands.UpdateUserRoles;

/// <summary>Replaces the business roles assigned to a user with exactly what was submitted.</summary>
public sealed record UpdateUserRolesCommand(string UserId, IReadOnlyList<string> Roles) : IRequest<UserDto>;
