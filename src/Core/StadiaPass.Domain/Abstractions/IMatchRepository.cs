using StadiaPass.Domain.Matches;

namespace StadiaPass.Domain.Abstractions;

public interface IMatchRepository : IRepository<Match>
{
    Task<IReadOnlyList<Match>> GetUpcomingAsync(
        DateTimeOffset fromUtc,
        string? categoryName = null,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the match together with its full seat map - use only for seat map screens.</summary>
    Task<Match?> GetWithSeatMapAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>Loads the match with a single seat attached, so a seat transition never pulls 20k rows.</summary>
    Task<Match?> GetWithSeatAsync(Guid matchId, string seatNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one seat moving from reserved to sold in the match counters, as a relative update the
    /// database works out for itself - <c>sold = sold + 1</c> - rather than as the totals this request
    /// happened to read. Two people buying two different seats of the same match would otherwise both write
    /// the same totals and one of the two sales would quietly vanish from the counts.
    /// </summary>
    /// <remarks>
    /// Call this inside a transaction, and before saving anything else: the counters the aggregate moved in
    /// memory are handed over to the database here, so the save that follows leaves them alone.
    /// </remarks>
    Task ApplySeatSaleToCountersAsync(Match match, CancellationToken cancellationToken = default);

    /// <summary>
    /// Matches that are holding seats whose time has run out, with only those seats attached. A hold is a
    /// promise with a deadline on it; nothing releases one when the deadline passes unless something goes
    /// looking, and until then the seat is unsellable and the counters disagree with the seat map.
    /// </summary>
    Task<IReadOnlyList<Match>> GetWithExpiredReservationsAsync(
        DateTimeOffset now,
        int maxMatches,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts released seats back into the counters, as a relative update the database works out for itself -
    /// the same reason and the same shape as <see cref="ApplySeatSaleToCountersAsync"/>.
    /// </summary>
    Task ApplySeatReleaseToCountersAsync(
        Match match,
        int releasedCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a voided sale back into the counters - a chargeback or a refund taking a seat off somebody. The
    /// same relative update as a sale and a release, pointing the third way.
    /// </summary>
    Task ApplySeatVoidToCountersAsync(
        Match match,
        int voidedCount,
        CancellationToken cancellationToken = default);

    /// <summary>Guards catalogue deletes: a venue or category in use by a match cannot be removed.</summary>
    Task<bool> ExistsForVenueAsync(Guid venueId, CancellationToken cancellationToken = default);

    Task<bool> ExistsForCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
