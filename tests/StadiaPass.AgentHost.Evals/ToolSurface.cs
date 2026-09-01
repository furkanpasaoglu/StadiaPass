using System.Reflection;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using StadiaPass.McpServer.Api;
using StadiaPass.McpServer.Tools;

namespace StadiaPass.AgentHost.Evals;

/// <summary>
/// The tool definitions the model is scored against, built by reflection from the real
/// <see cref="CatalogueTools"/> - names from the <see cref="McpServerToolAttribute"/>, descriptions and
/// parameter schemas from the same attributes production serves. A hand-written copy here would drift the
/// day somebody rewrites a description, and a drifted eval measures a tool surface that no longer exists.
/// </summary>
/// <remarks>
/// Selection is judged, execution is not: the catalogue client behind the tools refuses to be called, so
/// a run that somehow executes a tool fails loudly instead of quietly measuring the wrong thing.
/// </remarks>
internal static class ToolSurface
{
    public static IReadOnlyList<AITool> Tools { get; } = Build();

    private static List<AITool> Build()
    {
        var target = new CatalogueTools(new RefusingCatalogueClient());

        return
        [
            .. typeof(CatalogueTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
                .Where(pair => pair.Attribute is not null)
                .Select(pair => (AITool)AIFunctionFactory.Create(
                    pair.Method,
                    target,
                    new AIFunctionFactoryOptions { Name = pair.Attribute!.Name }))
        ];
    }

    private sealed class RefusingCatalogueClient : ICatalogueApiClient
    {
        public Task<IReadOnlyList<MatchSummary>> GetUpcomingMatchesAsync(
            string? category, CancellationToken cancellationToken = default) => throw Refusal();

        public Task<MatchSearchResult?> SearchMatchesAsync(
            string term, CancellationToken cancellationToken = default) => throw Refusal();

        public Task<SeatMap?> GetSeatMapAsync(
            Guid matchId, CancellationToken cancellationToken = default) => throw Refusal();

        private static NotSupportedException Refusal() =>
            new("The eval measures tool selection only; nothing should ever execute a tool.");
    }
}
