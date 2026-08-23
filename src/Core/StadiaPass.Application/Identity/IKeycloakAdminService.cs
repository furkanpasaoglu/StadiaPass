namespace StadiaPass.Application.Identity;

/// <summary>
/// Thin port over the Keycloak Admin REST API. StadiaPass keeps no role or permission tables of its own:
/// the identity provider is the system of record, and this abstraction is the only way in.
/// </summary>
public interface IKeycloakAdminService
{
    Task<IReadOnlyList<KeycloakRole>> GetRealmRolesAsync(CancellationToken cancellationToken = default);

    Task<KeycloakRole?> FindRealmRoleAsync(string name, CancellationToken cancellationToken = default);

    Task<KeycloakRole> CreateRealmRoleAsync(
        string name,
        string? description,
        CancellationToken cancellationToken = default);

    Task DeleteRealmRoleAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KeycloakRole>> GetRoleCompositesAsync(
        string roleId,
        CancellationToken cancellationToken = default);

    Task AddRoleCompositesAsync(
        string roleId,
        IReadOnlyCollection<KeycloakRole> composites,
        CancellationToken cancellationToken = default);

    Task RemoveRoleCompositesAsync(
        string roleId,
        IReadOnlyCollection<KeycloakRole> composites,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KeycloakUser>> GetUsersAsync(
        string? search,
        int first,
        int max,
        CancellationToken cancellationToken = default);

    Task<KeycloakUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<string> CreateUserAsync(NewKeycloakUser user, CancellationToken cancellationToken = default);

    Task UpdateUserAsync(
        string userId,
        string? email,
        string? firstName,
        string? lastName,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task DeleteUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KeycloakRole>> GetUserRealmRolesAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task AssignUserRealmRolesAsync(
        string userId,
        IReadOnlyCollection<KeycloakRole> roles,
        CancellationToken cancellationToken = default);

    Task RemoveUserRealmRolesAsync(
        string userId,
        IReadOnlyCollection<KeycloakRole> roles,
        CancellationToken cancellationToken = default);
}

public sealed record KeycloakRole(string Id, string Name, string? Description, bool Composite);

public sealed record KeycloakUser(
    string Id,
    string Username,
    string? Email,
    string? FirstName,
    string? LastName,
    bool Enabled);

public sealed record NewKeycloakUser(
    string Username,
    string? Email,
    string? FirstName,
    string? LastName,
    string Password,
    bool Enabled = true);
