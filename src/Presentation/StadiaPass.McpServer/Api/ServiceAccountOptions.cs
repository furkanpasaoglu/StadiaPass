using System.ComponentModel.DataAnnotations;

namespace StadiaPass.McpServer.Api;

/// <summary>
/// The identity this server calls the API with. It has one, unlike the browsing tools, because the
/// analytics endpoint asks who is calling - and the honest answer is "the MCP server", not "whoever
/// happened to connect an assistant to it".
/// </summary>
/// <remarks>
/// The secret is deliberately not defaulted. Without it the analytics tool is never registered and the
/// server stays what it has always been: three anonymous read-only tools over the public catalogue. A
/// missing secret should narrow what is offered, not produce a tool that fails when somebody calls it.
/// </remarks>
public sealed class ServiceAccountOptions
{
    public const string SectionName = "Keycloak";

    [Required]
    public string ServiceName { get; init; } = "keycloak";

    [Required]
    public string Realm { get; init; } = "stadiapass";

    [Required]
    public string McpClientId { get; init; } = "stadiapass-mcp";

    /// <summary>From Vault in a running system. Empty means "this server has no identity today".</summary>
    public string McpClientSecret { get; init; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(McpClientSecret);

    public Uri TokenEndpoint => new(
        $"https+http://{ServiceName}/realms/{Realm}/protocol/openid-connect/token");
}
