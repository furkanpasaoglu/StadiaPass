using System.ComponentModel.DataAnnotations;

namespace StadiaPass.AgentHost;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Where the StadiaPass MCP server answers; the AppHost injects the real endpoint.</summary>
    [Required]
    public string McpEndpoint { get; init; } = "http://localhost:5299/mcp";

    /// <summary>
    /// Local Ollama. Not an Aspire resource on purpose: the models weigh gigabytes and live in the
    /// developer's own Ollama install, which survives every `aspire run` the way a database volume does not.
    /// </summary>
    [Required]
    public string OllamaEndpoint { get; init; } = "http://localhost:11434";

    /// <summary>
    /// A local model that can call tools. Everything above the <c>IChatClient</c> seam is provider-agnostic,
    /// so swapping this for a cloud model is configuration, not code.
    /// </summary>
    [Required]
    public string Model { get; init; } = "qwen3:30b-a3b";
}
