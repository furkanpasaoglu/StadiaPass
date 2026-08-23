using System.ComponentModel.DataAnnotations;

namespace StadiaPass.WebMVC.Models;

public sealed record RoleSummary(string Id, string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record PermissionGroupSummary(string Name, IReadOnlyList<string> Permissions);

public sealed record RoleList(IReadOnlyList<RoleSummary> Roles, IReadOnlyList<PermissionGroupSummary> PermissionCatalogue);

public sealed record UserSummary(
    string Id,
    string Username,
    string? Email,
    string? FirstName,
    string? LastName,
    bool Enabled,
    IReadOnlyList<string> Roles);

public sealed record UserList(IReadOnlyList<UserSummary> Users, IReadOnlyList<string> AssignableRoles);

public sealed class CreateRoleInput
{
    [Required]
    [StringLength(64)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9._-]*$",
        ErrorMessage = "Role name must start with a letter and may contain letters, digits, dot, dash and underscore.")]
    [Display(Name = "Role name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(255)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Permissions")]
    public List<string> Permissions { get; set; } = [];
}

public sealed class RolePermissionsInput
{
    [Required]
    public string RoleName { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = [];
}

public sealed class CreateUserInput
{
    [Required]
    [StringLength(64)]
    [RegularExpression("^[a-z0-9._-]+$",
        ErrorMessage = "Username may contain lowercase letters, digits, dot, dash and underscore only.")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [EmailAddress]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(64)]
    [Display(Name = "First name")]
    public string? FirstName { get; set; }

    [StringLength(64)]
    [Display(Name = "Last name")]
    public string? LastName { get; set; }

    [Required]
    [StringLength(128, MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Role")]
    public string? Role { get; set; }
}

public sealed class RoleEditorViewModel
{
    public required IReadOnlyList<RoleSummary> Roles { get; init; }

    public required IReadOnlyList<PermissionGroupSummary> PermissionCatalogue { get; init; }

    public RoleSummary? Selected { get; init; }
}

public sealed class UserPortalViewModel
{
    public required IReadOnlyList<UserSummary> Users { get; init; }

    public required IReadOnlyList<string> AssignableRoles { get; init; }

    public string? Search { get; init; }
}
