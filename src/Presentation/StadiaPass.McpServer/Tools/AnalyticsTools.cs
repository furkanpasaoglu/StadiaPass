using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using StadiaPass.McpServer.Api;

namespace StadiaPass.McpServer.Tools;

/// <summary>
/// The takings of a fixture - the one thing here an anonymous visitor cannot see, and the reason this
/// server has a service account at all.
/// </summary>
/// <remarks>
/// <para>
/// A class of its own rather than a fourth method on <see cref="CatalogueTools"/>, because the two have
/// different answers to "who may call this". The catalogue tools are the public site in another shape;
/// this one is staff-only and is registered only when a service-account secret has been configured. No
/// secret, no tool - an AI client is told the truth about what it can ask rather than handed a tool that
/// fails when it tries.
/// </para>
/// <para>
/// What it does <b>not</b> do is decide what revenue means. Net revenue excludes refunded tickets, and
/// that rule lives in the query handler behind the API, in code a test can break - never in a tool
/// description a model is asked to honour.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class AnalyticsTools(ICatalogueApiClient catalogue)
{
    [McpServerTool(Name = "get_match_revenue", ReadOnly = true)]
    [Description(
        "Reports what one match has taken: tickets sold and their net revenue, tickets refunded and the "
        + "amount given back, plus capacity, seats sold and occupancy. Net revenue already excludes "
        + "refunded tickets - do not subtract them again. Use a match id from get_upcoming_matches or "
        + "search_matches. Staff-facing figures, not for quoting to customers.")]
    public async Task<MatchRevenue> GetMatchRevenueAsync(
        [Description("The match id (GUID) whose takings are being asked about.")]
        Guid matchId,
        CancellationToken cancellationToken = default) =>
        await catalogue.GetMatchRevenueAsync(matchId, cancellationToken)
        ?? throw new McpException($"No match with id {matchId} exists.");
}
