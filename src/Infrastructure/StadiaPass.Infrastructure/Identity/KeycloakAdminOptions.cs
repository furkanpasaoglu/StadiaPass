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

    [Required]
    public string AdminClientSecret { get; init; } = "stadiapass-admin-dev-secret";
}
