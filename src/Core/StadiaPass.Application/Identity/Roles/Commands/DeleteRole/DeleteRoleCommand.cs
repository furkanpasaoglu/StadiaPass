using MediatR;

namespace StadiaPass.Application.Identity.Roles.Commands.DeleteRole;

public sealed record DeleteRoleCommand(string RoleName) : IRequest;
