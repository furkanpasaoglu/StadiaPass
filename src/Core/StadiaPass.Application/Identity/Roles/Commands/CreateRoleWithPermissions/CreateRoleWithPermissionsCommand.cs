using MediatR;

namespace StadiaPass.Application.Identity.Roles.Commands.CreateRoleWithPermissions;

/// <summary>
/// Creates a business role in Keycloak and binds the selected permissions to it as composites, so a token
/// issued to a member of the role carries every one of those permission strings.
/// </summary>
public sealed record CreateRoleWithPermissionsCommand(
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions) : IRequest<RoleDto>;
