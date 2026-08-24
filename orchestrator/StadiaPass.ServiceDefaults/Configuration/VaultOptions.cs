namespace StadiaPass.ServiceDefaults.Configuration;

/// <summary>
/// Where the secrets live and how to reach them. Everything here is address and policy - never a secret
/// itself. The token is the one credential the process is given, and it arrives from the environment.
/// </summary>
public sealed class VaultOptions
{
    public const string SectionName = "Vault";

    /// <summary>Base address of the Vault server, e.g. <c>http://localhost:8200</c>.</summary>
    public string? Address { get; init; }

    /// <summary>
    /// The token this process authenticates with. In development the orchestrator hands out the dev root
    /// token; a deployment issues a scoped token through AppRole, Kubernetes auth or the agent sidecar.
    /// </summary>
    public string? Token { get; init; }

    /// <summary>Mount point of the KV v2 engine. <c>secret</c> is what a dev-mode server mounts.</summary>
    public string MountPoint { get; init; } = "secret";

    /// <summary>Path under the engine holding this application's secrets.</summary>
    public string Path { get; init; } = "stadiapass";

    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// How long to keep retrying a path that is not there yet. A freshly started Vault is empty until
    /// something writes to it, and a restart can put the two out of step by a second or two.
    /// </summary>
    public int StartupTimeoutSeconds { get; init; } = 30;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Address) && !string.IsNullOrWhiteSpace(Token);
}
