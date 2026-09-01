namespace StadiaPass.McpServer.Api;

/// <summary>
/// The slice of the StadiaPass API this server is allowed to reach: the three endpoints a visitor can
/// browse without signing in. Everything the tools expose comes through here, so the list below is also
/// the complete inventory of what an AI client can do - read the catalogue, nothing else.
/// </summary>
public interface ICatalogueApiClient
{
    Task<IReadOnlyList<MatchSummary>> GetUpcomingMatchesAsync(
        string? category,
        CancellationToken cancellationToken = default);

    Task<MatchSearchResult?> SearchMatchesAsync(string term, CancellationToken cancellationToken = default);

    /// <returns><see langword="null"/> when the match does not exist.</returns>
    Task<SeatMap?> GetSeatMapAsync(Guid matchId, CancellationToken cancellationToken = default);
}
