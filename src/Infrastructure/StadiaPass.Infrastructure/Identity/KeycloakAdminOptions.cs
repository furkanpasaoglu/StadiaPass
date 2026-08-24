using System.ComponentModel.DataAnnotations;

namespace StadiaPass.Infrastructure.Identity;

public sealed class KeycloakAdminOptions
{
    public const string SectionName = "Keycloak";

    [Required]
    public string Realm { get; init; } = "stadiapass";

    /// <summary>
    /// Confidential client whose service account carries the realm-management roles. Using a scoped service
    /// account keeps the master realm admin password out of the application entirely.
    /// </summary>
    [Required]
    public string AdminClientId { get; init; } = "stadiapass-admin-api";

    /// <summary>
    /// Read from configuration, which in a running system means Vault - there is deliberately no default.
    /// A secret with a fallback is a secret that quietly keeps working after someone forgets to set it.
    /// </summary>
    [Required(ErrorMessage = "Keycloak:AdminClientSecret is not set. It is expected to come from Vault.")]
    public string AdminClientSecret { get; init; } = string.Empty;
}
