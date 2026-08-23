using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StadiaPass.Application.Identity;

namespace StadiaPass.Infrastructure.Identity;

/// <summary>
/// Adapter over the Keycloak Admin REST API. StadiaPass stores no roles or users of its own, so every call
/// here goes straight to the identity provider.
/// </summary>
internal sealed class KeycloakAdminService(
    HttpClient httpClient,
    KeycloakAdminTokenProvider tokenProvider,
    IOptions<KeycloakAdminOptions> options) : IKeycloakAdminService
{
    public const string HttpClientName = "keycloak-admin";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private string RealmBase => $"/admin/realms/{options.Value.Realm}";

    public async Task<IReadOnlyList<KeycloakRole>> GetRealmRolesAsync(
        CancellationToken cancellationToken = default) =>
        await GetRolesAsync($"{RealmBase}/roles?briefRepresentation=true&max=1000", cancellationToken);

    public async Task<KeycloakRole?> FindRealmRoleAsync(string name, CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Get, $"{RealmBase}/roles/{Uri.EscapeDataString(name)}", cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var role = await response.Content.ReadFromJsonAsync<RoleRepresentation>(SerializerOptions, cancellationToken);

        return role?.ToRole();
    }

    public async Task<KeycloakRole> CreateRealmRoleAsync(
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, $"{RealmBase}/roles", cancellationToken);
        request.Content = JsonContent.Create(new { name, description }, options: SerializerOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await FindRealmRoleAsync(name, cancellationToken)
               ?? throw new InvalidOperationException($"Role '{name}' was created but could not be read back.");
    }

    public async Task DeleteRealmRoleAsync(string name, CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Delete, $"{RealmBase}/roles/{Uri.EscapeDataString(name)}", cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<KeycloakRole>> GetRoleCompositesAsync(
        string roleId,
        CancellationToken cancellationToken = default) =>
        await GetRolesAsync($"{RealmBase}/roles-by-id/{roleId}/composites/realm", cancellationToken);

    public Task AddRoleCompositesAsync(
        string roleId,
        IReadOnlyCollection<KeycloakRole> composites,
        CancellationToken cancellationToken = default) =>
        SendRolesAsync(HttpMethod.Post, $"{RealmBase}/roles-by-id/{roleId}/composites", composites, cancellationToken);

    public Task RemoveRoleCompositesAsync(
        string roleId,
        IReadOnlyCollection<KeycloakRole> composites,
        CancellationToken cancellationToken = default) =>
        SendRolesAsync(HttpMethod.Delete, $"{RealmBase}/roles-by-id/{roleId}/composites", composites, cancellationToken);

    public async Task<IReadOnlyList<KeycloakUser>> GetUsersAsync(
        string? search,
        int first,
        int max,
        CancellationToken cancellationToken = default)
    {
        var route = $"{RealmBase}/users?first={first}&max={max}&briefRepresentation=true";

        if (!string.IsNullOrWhiteSpace(search))
        {
            route += $"&search={Uri.EscapeDataString(search)}";
        }

        using var request = await CreateRequestAsync(HttpMethod.Get, route, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var users = await response.Content.ReadFromJsonAsync<List<UserRepresentation>>(
            SerializerOptions, cancellationToken);

        return [.. (users ?? []).Select(user => user.ToUser())];
    }

    public async Task<KeycloakUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"{RealmBase}/users/{userId}", cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var user = await response.Content.ReadFromJsonAsync<UserRepresentation>(SerializerOptions, cancellationToken);

        return user?.ToUser();
    }

    public async Task<string> CreateUserAsync(NewKeycloakUser user, CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, $"{RealmBase}/users", cancellationToken);
        request.Content = JsonContent.Create(
            new
            {
                username = user.Username,
                email = user.Email,
                firstName = user.FirstName,
                lastName = user.LastName,
                enabled = user.Enabled,
                emailVerified = true,
                credentials = new[] { new { type = "password", value = user.Password, temporary = false } }
            },
            options: SerializerOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        // Keycloak returns the new identifier in the Location header rather than in a body.
        if (response.Headers.Location is { Segments.Length: > 0 } location)
        {
            return location.Segments[^1].Trim('/');
        }

        var created = await GetUsersAsync(user.Username, 0, 1, cancellationToken);

        return created.Count is not 0
            ? created[0].Id
            : throw new InvalidOperationException("Keycloak did not return the identifier of the new user.");
    }

    public async Task UpdateUserAsync(
        string userId,
        string? email,
        string? firstName,
        string? lastName,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Put, $"{RealmBase}/users/{userId}", cancellationToken);
        request.Content = JsonContent.Create(new { email, firstName, lastName, enabled }, options: SerializerOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Delete, $"{RealmBase}/users/{userId}", cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<KeycloakRole>> GetUserRealmRolesAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        await GetRolesAsync($"{RealmBase}/users/{userId}/role-mappings/realm", cancellationToken);

    public Task AssignUserRealmRolesAsync(
        string userId,
        IReadOnlyCollection<KeycloakRole> roles,
        CancellationToken cancellationToken = default) =>
        SendRolesAsync(HttpMethod.Post, $"{RealmBase}/users/{userId}/role-mappings/realm", roles, cancellationToken);

    public Task RemoveUserRealmRolesAsync(
        string userId,
        IReadOnlyCollection<KeycloakRole> roles,
        CancellationToken cancellationToken = default) =>
        SendRolesAsync(HttpMethod.Delete, $"{RealmBase}/users/{userId}/role-mappings/realm", roles, cancellationToken);

    private async Task<IReadOnlyList<KeycloakRole>> GetRolesAsync(string route, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, route, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return [];
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var roles = await response.Content.ReadFromJsonAsync<List<RoleRepresentation>>(
            SerializerOptions, cancellationToken);

        return [.. (roles ?? []).Select(role => role.ToRole())];
    }

    private async Task SendRolesAsync(
        HttpMethod method,
        string route,
        IReadOnlyCollection<KeycloakRole> roles,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(method, route, cancellationToken);
        request.Content = JsonContent.Create(
            roles.Select(role => new { id = role.Id, name = role.Name }).ToArray(),
            options: SerializerOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        string route,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(route, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await tokenProvider.GetAsync(cancellationToken));

        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        throw new KeycloakAdminException(
            $"Keycloak Admin API returned {(int)response.StatusCode} for {response.RequestMessage?.RequestUri}: {body}",
            response.StatusCode);
    }

    private sealed record RoleRepresentation(string Id, string Name, string? Description, bool Composite)
    {
        public KeycloakRole ToRole() => new(Id, Name, Description, Composite);
    }

    private sealed record UserRepresentation(
        string Id,
        string Username,
        string? Email,
        string? FirstName,
        string? LastName,
        bool Enabled)
    {
        public KeycloakUser ToUser() => new(Id, Username, Email, FirstName, LastName, Enabled);
    }
}

public sealed class KeycloakAdminException(string message, HttpStatusCode statusCode) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
