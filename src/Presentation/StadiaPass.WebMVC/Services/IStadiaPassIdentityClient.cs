using StadiaPass.WebMVC.Models;

namespace StadiaPass.WebMVC.Services;

/// <summary>Identity portal calls. The MVC app never talks to Keycloak directly - the API brokers them.</summary>
public interface IStadiaPassIdentityClient
{
    Task<RoleList> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<RoleSummary>> CreateRoleAsync(CreateRoleInput input, CancellationToken cancellationToken = default);

    Task<ApiResult<RoleSummary>> UpdateRolePermissionsAsync(
        string roleName,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default);

    Task<ApiResult<bool>> DeleteRoleAsync(string roleName, CancellationToken cancellationToken = default);

    Task<UserList> GetUsersAsync(string? search, CancellationToken cancellationToken = default);

    Task<ApiResult<UserSummary>> CreateUserAsync(CreateUserInput input, CancellationToken cancellationToken = default);

    Task<ApiResult<UserSummary>> UpdateUserRolesAsync(
        string userId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<ApiResult<UserSummary>> UpdateUserAsync(
        string userId,
        string? email,
        string? firstName,
        string? lastName,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<ApiResult<bool>> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);
}
