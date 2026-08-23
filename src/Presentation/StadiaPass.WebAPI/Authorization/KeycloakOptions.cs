using System.ComponentModel.DataAnnotations;

namespace StadiaPass.WebAPI.Authorization;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Aspire resource name of the Keycloak container; resolved through service discovery.</summary>
    [Required]
    public string ServiceName { get; init; } = "keycloak";

    [Required]
    public string Realm { get; init; } = "stadiapass";

    /// <summary>Client id this API is issued for; also the <c>aud</c> claim it validates.</summary>
    [Required]
    public string Audience { get; init; } = "stadiapass-api";

    /// <summary>Public client used by the Scalar reference to run the authorization code flow.</summary>
    [Required]
    public string ScalarClientId { get; init; } = "stadiapass-scalar";

    /// <summary>Externally reachable Keycloak base URL, used for the browser-facing OAuth2 URLs.</summary>
    [Required]
    public string PublicAuthority { get; init; } = "https://localhost:8080";

    public Uri AuthorizationUrl =>
        new($"{PublicAuthority.TrimEnd('/')}/realms/{Realm}/protocol/openid-connect/auth");

    public Uri TokenUrl =>
        new($"{PublicAuthority.TrimEnd('/')}/realms/{Realm}/protocol/openid-connect/token");
}
