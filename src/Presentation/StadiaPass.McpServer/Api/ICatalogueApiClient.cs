namespace StadiaPass.McpServer.Api;

/// <summary>
/// The slice of the StadiaPass API this server is allowed to reach. Everything the tools expose comes
/// through here, so the list below is also the complete inventory of what an AI client can do: browse the
/// public catalogue, and - only where this server has been given an identity - read what a fixture has
/// taken. Every one of them is a read.
/// </summary>
public interface ICatalogueApiClient
{
    Task<IReadOnlyList<MatchSummary>> GetUpcomingMatchesAsync(
        string? category,
        CancellationToken cancellationToken = default);

    Task<MatchSearchResult?> SearchMatchesAsync(string term, CancellationToken cancellationToken = default);

    /// <returns><see langword="null"/> when the match does not exist.</returns>
    Task<SeatMap?> GetSeatMapAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a fixture has taken. The only call here the API asks for a permission on, which is why this
    /// server carries a service-account token at all.
    /// </summary>
    /// <returns><see langword="null"/> when the match does not exist.</returns>
    Task<MatchRevenue?> GetMatchRevenueAsync(Guid matchId, CancellationToken cancellationToken = default);
}
