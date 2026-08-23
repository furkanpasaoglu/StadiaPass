using System.ComponentModel.DataAnnotations;

namespace StadiaPass.WebMVC.Authentication;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    [Required]
    public string ServiceName { get; init; } = "keycloak";

    [Required]
    public string Realm { get; init; } = "stadiapass";

    [Required]
    public string ClientId { get; init; } = "stadiapass-mvc";

    [Required]
    public string ClientSecret { get; init; } = "stadiapass-mvc-dev-secret";

    /// <summary>Client id of the API, used when reading client roles out of the token.</summary>
    [Required]
    public string ApiClientId { get; init; } = "stadiapass-api";

    [Required]
    public string PublicAuthority { get; init; } = "https://localhost:8080";
}
