using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using StadiaPass.McpServer.Api;

namespace StadiaPass.McpServer.Tools;

/// <summary>
/// The read-only face of the catalogue, for AI clients. Every tool here answers over the same public API
/// endpoints an anonymous visitor browses - no database access, no credentials, nothing an unauthenticated
/// request could not already see. Anything that changes state (holding a seat, cancelling a match) is
/// deliberately absent and arrives, if ever, as separate tools behind real authorization.
/// </summary>
[McpServerToolType]
internal sealed class CatalogueTools(ICatalogueApiClient catalogue)
{
    [McpServerTool(Name = "get_upcoming_matches", ReadOnly = true)]
    [Description(
        "Lists the upcoming matches currently on sale, soonest kick-off first, with seat availability "
        + "counts. Optionally filtered to one sport category.")]
    public async Task<IReadOnlyList<MatchSummary>> GetUpcomingMatchesAsync(
        [Description("Sport category name to filter by, e.g. 'Football'. Omit to list every sport.")]
        string? category = null,
        CancellationToken cancellationToken = default) =>
        await catalogue.GetUpcomingMatchesAsync(category, cancellationToken);

    [McpServerTool(Name = "search_matches", ReadOnly = true)]
    [Description(
        "Free-text search over the matches on sale - team, venue, city or sport - most relevant first. "
        + "When 'searchAvailable' is false in the answer, the search index could not be reached and the "
        + "matches given are the plain upcoming listing instead of real search results: say so.")]
    public async Task<MatchSearchResult> SearchMatchesAsync(
        [Description("What to look for: a team, venue, city or sport, in any language the catalogue uses.")]
        string query,
        CancellationToken cancellationToken = default) =>
        await catalogue.SearchMatchesAsync(query, cancellationToken)
            ?? throw new McpException("The search returned an empty response.");

    [McpServerTool(Name = "get_seat_availability", ReadOnly = true)]
    [Description(
        "Summarises seat availability for one match: total seats left, the cheapest available price, and "
        + "per-block counts with price ranges. Use a match id from get_upcoming_matches or search_matches. "
        + "This is a summary on purpose - seat-by-seat choice happens on the seat map in the web UI.")]
    public async Task<SeatAvailabilitySummary> GetSeatAvailabilityAsync(
        [Description("The match id (GUID) whose availability is being asked about.")]
        Guid matchId,
        CancellationToken cancellationToken = default)
    {
        var map = await catalogue.GetSeatMapAsync(matchId, cancellationToken)
            ?? throw new McpException($"No match with id {matchId} exists.");

        return SeatAvailabilitySummary.FromSeatMap(map);
    }
}
