namespace StadiaPass.Application.Identity;

/// <summary>A business role (a Keycloak composite realm role) together with the permissions bound to it.</summary>
public sealed record RoleDto(
    string Id,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions);

public sealed record RoleListDto(
    IReadOnlyList<RoleDto> Roles,
    IReadOnlyList<PermissionGroupDto> PermissionCatalogue);

public sealed record PermissionGroupDto(string Name, IReadOnlyList<string> Permissions);

public sealed record UserDto(
    string Id,
    string Username,
    string? Email,
    string? FirstName,
    string? LastName,
    bool Enabled,
    IReadOnlyList<string> Roles);

public sealed record UserListDto(IReadOnlyList<UserDto> Users, IReadOnlyList<string> AssignableRoles);
