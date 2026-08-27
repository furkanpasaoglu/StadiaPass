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

    /// <summary>
    /// Read from configuration, which in a running system means Vault - there is deliberately no default.
    /// </summary>
    [Required(ErrorMessage = "Keycloak:ClientSecret is not set. It is expected to come from Vault.")]
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>Client id of the API, used when reading client roles out of the token.</summary>
    [Required]
    public string ApiClientId { get; init; } = "stadiapass-api";

    // There is deliberately no PublicAuthority here. The API needs one, because Scalar runs an OAuth flow in
    // the reader's browser and the browser cannot resolve a container name. This application does not: the
    // OIDC handler is registered against the Aspire resource name, and the address service discovery hands
    // back is the one the browser is redirected to. The property used to exist, marked [Required], with the
    // AppHost passing an environment variable to fill it - and nothing ever read it. A required-looking
    // setting that changes nothing is worse than no setting, because the next person to debug a redirect
    // will change it first and learn nothing from the result.
}
