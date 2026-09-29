using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace StadiaPass.AgentHost.Policy;

/// <summary>
/// Retrieval through the MCP server's <c>search_policies</c> tool, called from code rather than by the
/// model.
/// </summary>
/// <remarks>
/// The same tool layer the analyst uses, consumed a different way: the analyst's model decides to call
/// a tool, this class simply calls one. Going through the MCP server rather than the API directly is what
/// keeps this host without a database, without a token and without a second copy of the API client - the
/// MCP server already holds the service account that may read the policies.
/// </remarks>
internal sealed class McpPolicyRetriever(IList<McpClientTool> tools) : IPolicyRetriever
{
    public const string ToolName = "search_policies";

    /// <summary>
    /// Wider than the three the model reads, so the reranker has a section ranked fourth or seventh by the
    /// vectors to choose from. Ten is also the most the search endpoint will hand back in one call.
    /// </summary>
    public const int CandidateCount = 10;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PolicyPassage>> RetrieveAsync(string question, CancellationToken cancellationToken)
    {
        var tool = tools.FirstOrDefault(candidate => candidate.Name == ToolName)
            ?? throw new InvalidOperationException(
                $"The MCP server offers no '{ToolName}' tool; it is only offered when the server has a "
                + "service-account secret to read the policies with.");

        var result = await tool.CallAsync(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["question"] = question, ["limit"] = CandidateCount },
            cancellationToken: cancellationToken);

        if (result.IsError is true)
        {
            throw new InvalidOperationException($"The '{ToolName}' tool answered with an error: {TextOf(result)}");
        }

        var payload = JsonSerializer.Deserialize<SearchResult>(TextOf(result), Json)
            ?? throw new InvalidOperationException($"The '{ToolName}' tool answered with an empty body.");

        return payload.Hits;
    }

    /// <summary>The tool's answer is one JSON text block, the way every tool on this server answers.</summary>
    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private sealed record SearchResult(string Question, IReadOnlyList<PolicyPassage> Hits);
}
