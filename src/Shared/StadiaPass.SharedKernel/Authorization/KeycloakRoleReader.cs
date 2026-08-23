using System.Text.Json;

namespace StadiaPass.SharedKernel.Authorization;

/// <summary>
/// Reads Keycloak's <c>realm_access</c> / <c>resource_access</c> JSON claims and yields the role names that
/// correspond to a known permission. Anything the application does not declare is discarded rather than
/// trusted, so adding an unrelated role in Keycloak can never widen access.
/// </summary>
public static class KeycloakRoleReader
{
    public const string RealmAccessClaim = "realm_access";
    public const string ResourceAccessClaim = "resource_access";

    private const string RolesProperty = "roles";

    public static IReadOnlyList<string> ReadPermissions(
        string? realmAccessJson,
        string? resourceAccessJson,
        string clientId) =>
    [
        .. ReadRealmRoles(realmAccessJson)
            .Concat(ReadClientRoles(resourceAccessJson, clientId))
            .Where(StadiaPassPermissions.IsDefined)
            .Distinct(StringComparer.Ordinal)
    ];

    private static List<string> ReadRealmRoles(string? json)
    {
        using var document = TryParse(json);

        return document is null ? [] : ReadRoles(document.RootElement);
    }

    private static List<string> ReadClientRoles(string? json, string clientId)
    {
        using var document = TryParse(json);

        return document?.RootElement.TryGetProperty(clientId, out var client) is true ? ReadRoles(client) : [];
    }

    private static List<string> ReadRoles(JsonElement element) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(RolesProperty, out var roles)
        && roles.ValueKind is JsonValueKind.Array
            ? [.. roles.EnumerateArray()
                .Where(role => role.ValueKind is JsonValueKind.String)
                .Select(role => role.GetString()!)]
            : [];

    private static JsonDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
