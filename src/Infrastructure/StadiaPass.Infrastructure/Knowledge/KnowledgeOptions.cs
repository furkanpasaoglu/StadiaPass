using System.ComponentModel.DataAnnotations;

namespace StadiaPass.Infrastructure.Knowledge;

/// <summary>
/// Where the policy documents come from and which model reads them.
/// </summary>
public sealed class KnowledgeOptions
{
    public const string SectionName = "Knowledge";

    /// <summary>
    /// Local Ollama, and not an Aspire resource for the same reason the agent's is not: the model weighs
    /// gigabytes and lives in the developer's own install.
    /// </summary>
    [Required]
    public string OllamaEndpoint { get; init; } = "http://localhost:11434";

    /// <summary>
    /// A multilingual embedding model, because the documents are Turkish and the questions will be too.
    /// Changing it changes every vector in the store; the loader notices and embeds everything again.
    /// </summary>
    [Required]
    public string EmbeddingModel { get; init; } = "bge-m3";

    /// <summary>
    /// The folder of Markdown documents loaded at start-up, relative to the application's own directory.
    /// The API project copies <c>docs/knowledge</c> there at build time.
    /// </summary>
    [Required]
    public string DocumentsPath { get; init; } = "knowledge";

    /// <summary>Embedding a section is a few hundred milliseconds warm; the first call also loads the model.</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 60;
}
