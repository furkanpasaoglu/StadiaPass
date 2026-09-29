using Microsoft.Agents.AI;

namespace StadiaPass.AgentHost.Support;

/// <summary>The analyst, as the desk sees it: a question in, an answer out.</summary>
/// <remarks>
/// A seam rather than the agent itself, so the desk can be tested without a model, an MCP server and a
/// catalogue behind it. The agent still does everything it does in DevUI - choosing tools, calling them,
/// writing the answer - behind the one method.
/// </remarks>
internal interface IAnalyst
{
    Task<string> AskAsync(string question, CancellationToken cancellationToken);
}

/// <summary>The real analyst: the same agent DevUI talks to, asked once, with no conversation kept.</summary>
internal sealed class AgentAnalyst(AIAgent agent) : IAnalyst
{
    public async Task<string> AskAsync(string question, CancellationToken cancellationToken)
    {
        var response = await agent.RunAsync(question, cancellationToken: cancellationToken);

        return response.Text.Trim();
    }
}
