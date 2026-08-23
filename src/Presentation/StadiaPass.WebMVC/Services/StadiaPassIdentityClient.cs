using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.WebMVC.Models;

namespace StadiaPass.WebMVC.Services;

internal sealed class StadiaPassIdentityClient(HttpClient httpClient) : IStadiaPassIdentityClient
{
    private static readonly RoleList EmptyRoles = new([], []);

    private static readonly UserList EmptyUsers = new([], []);

    public async Task<RoleList> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(new Uri("/api/v1/roles", UriKind.Relative), cancellationToken);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<RoleList>(cancellationToken) ?? EmptyRoles
            : EmptyRoles;
    }

    public async Task<ApiResult<RoleSummary>> CreateRoleAsync(
        CreateRoleInput input,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/roles",
            new { name = input.Name, description = input.Description, permissions = input.Permissions },
            cancellationToken);

        return await ReadAsync<RoleSummary>(response, cancellationToken);
    }

    public async Task<ApiResult<RoleSummary>> UpdateRolePermissionsAsync(
        string roleName,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/v1/roles/{Uri.EscapeDataString(roleName)}/permissions",
            new { permissions },
            cancellationToken);

        return await ReadAsync<RoleSummary>(response, cancellationToken);
    }

    public async Task<ApiResult<bool>> DeleteRoleAsync(string roleName, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            new Uri($"/api/v1/roles/{Uri.EscapeDataString(roleName)}", UriKind.Relative), cancellationToken);

        return await ReadEmptyAsync(response, cancellationToken);
    }

    public async Task<UserList> GetUsersAsync(string? search, CancellationToken cancellationToken = default)
    {
        var route = string.IsNullOrWhiteSpace(search)
            ? "/api/v1/users"
            : $"/api/v1/users?search={Uri.EscapeDataString(search)}";

        var response = await httpClient.GetAsync(new Uri(route, UriKind.Relative), cancellationToken);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<UserList>(cancellationToken) ?? EmptyUsers
            : EmptyUsers;
    }

    public async Task<ApiResult<UserSummary>> CreateUserAsync(
        CreateUserInput input,
        CancellationToken cancellationToken = default)
    {
        string[] roles = string.IsNullOrWhiteSpace(input.Role) ? [] : [input.Role];

        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/users",
            new
            {
                username = input.Username,
                email = input.Email,
                firstName = input.FirstName,
                lastName = input.LastName,
                password = input.Password,
                roles
            },
            cancellationToken);

        return await ReadAsync<UserSummary>(response, cancellationToken);
    }

    public async Task<ApiResult<UserSummary>> UpdateUserRolesAsync(
        string userId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/v1/users/{userId}/roles", new { roles }, cancellationToken);

        return await ReadAsync<UserSummary>(response, cancellationToken);
    }

    public async Task<ApiResult<UserSummary>> UpdateUserAsync(
        string userId,
        string? email,
        string? firstName,
        string? lastName,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/v1/users/{userId}",
            new { email, firstName, lastName, enabled },
            cancellationToken);

        return await ReadAsync<UserSummary>(response, cancellationToken);
    }

    public async Task<ApiResult<bool>> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            new Uri($"/api/v1/users/{userId}", UriKind.Relative), cancellationToken);

        return await ReadEmptyAsync(response, cancellationToken);
    }

    private static async Task<ApiResult<bool>> ReadEmptyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return ApiResult.Success(true);
        }

        var problem = await TryReadProblemAsync(response, cancellationToken);

        return ApiResult.Failure<bool>(
            problem?.Detail ?? problem?.Title ?? $"The API responded with {(int)response.StatusCode}.");
    }

    private static async Task<ApiResult<T>> ReadAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

            return value is null
                ? ApiResult.Failure<T>("The API returned an empty response.")
                : ApiResult.Success(value);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden)
        {
            return ApiResult.Failure<T>("Your account does not carry the permission required for this action.");
        }

        var problem = await TryReadProblemAsync(response, cancellationToken);

        return ApiResult.Failure<T>(
            problem?.Detail ?? problem?.Title ?? $"The API responded with {(int)response.StatusCode}.",
            (problem as ValidationProblemDetails)?.Errors.ToDictionary(entry => entry.Key, entry => entry.Value));
    }

    private static async Task<ProblemDetails?> TryReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return response.StatusCode is HttpStatusCode.BadRequest
                ? await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken)
                : await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
