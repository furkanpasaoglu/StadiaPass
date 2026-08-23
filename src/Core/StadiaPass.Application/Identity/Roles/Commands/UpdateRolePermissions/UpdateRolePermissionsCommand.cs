using MediatR;

namespace StadiaPass.Application.Identity.Roles.Commands.UpdateRolePermissions;

/// <summary>Replaces the permission set bound to a business role with exactly what the checklist submitted.</summary>
public sealed record UpdateRolePermissionsCommand(string RoleName, IReadOnlyList<string> Permissions)
    : IRequest<RoleDto>;
