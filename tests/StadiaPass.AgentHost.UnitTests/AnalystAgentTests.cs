using Microsoft.Extensions.AI;
using StadiaPass.AgentHost.Policy;

namespace StadiaPass.AgentHost.UnitTests;

/// <summary>
/// The analyst takes whatever the MCP server lists, so a tool added there for somebody else turns up in
/// front of the analyst's model too. Policy search is that tool: it belongs to the policy assistant, and
/// the analyst's evals do not measure it.
/// </summary>
public sealed class AnalystAgentTests
{
    [Fact]
    public void ToolsFrom_LeavesPolicySearchOut()
    {
        AITool[] offered = [Tool("search_matches"), Tool(McpPolicyRetriever.ToolName), Tool("get_match_revenue")];

        var tools = AnalystAgent.ToolsFrom(offered);

        tools.Select(tool => tool.Name).Should().Equal("search_matches", "get_match_revenue");
    }

    [Fact]
    public void ToolsFrom_KeepsEverythingElseInTheOrderItWasOffered()
    {
        AITool[] offered = [Tool("get_upcoming_matches"), Tool("search_matches"), Tool("get_seat_availability")];

        var tools = AnalystAgent.ToolsFrom(offered);

        tools.Select(tool => tool.Name).Should().Equal("get_upcoming_matches", "search_matches", "get_seat_availability");
    }

    private static AIFunction Tool(string name) =>
        AIFunctionFactory.Create(() => "unused", new AIFunctionFactoryOptions { Name = name });
}
