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
    /// <returns>
    /// The update itself, left for the caller to run. See the note on <see cref="PrepareSeatReleaseCounters"/>
    /// for why it is handed back rather than run here.
    /// </returns>
    Func<CancellationToken, Task> PrepareSeatSaleCounters(Match match);

    /// <summary>
    /// Records seats moving from available to reserved, as the same relative update as every other counter
    /// method here. A hold moves the counters just as a sale does, so leaving this one to be written from
    /// memory would undo the very thing the others are for: two people holding two different seats of the
    /// same match would both write the totals they read, and one of the two holds would vanish from the
    /// counts - as would any sale that committed in between.
    /// </summary>
    /// <param name="reservedCount">
    /// What <see cref="Match.SeatsClaimedByReserving"/> answered before the transition. Taking over a hold
    /// that had already run out moves nothing, so this is not always one.
    /// </param>
    Func<CancellationToken, Task> PrepareSeatReservationCounters(Match match, int reservedCount);

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
    /// the same reason and the same shape as <see cref="PrepareSeatSaleCounters"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every one of these comes in two halves, and they belong on opposite sides of the save. Calling the
    /// method takes the counters the aggregate moved in memory out of the save's hands, so it has to happen
    /// first. Running what it returns issues the relative update, and that has to happen <b>last</b> - after
    /// the save, immediately before the commit.
    /// </para>
    /// <para>
    /// The reason is the match row. It is the coarsest lock in the system: one row per fixture, taken by
    /// every write that touches any of its seats, and held until the transaction commits. Take it at the top
    /// and every sale of one match queues behind the seat write, the ticket insert and the outbox insert of
    /// the sale in front of it. Take it at the bottom and it is held for the commit alone. The seat rows are
    /// written first either way, so the order two transactions reach for things in is still the same for all
    /// of them, which is what keeps deadlocks out.
    /// </para>
    /// </remarks>
    Func<CancellationToken, Task> PrepareSeatReleaseCounters(Match match, int releasedCount);

    /// <summary>
    /// Puts a voided sale back into the counters - a chargeback or a refund taking a seat off somebody. The
    /// same relative update as a sale and a release, pointing the third way.
    /// </summary>
    Func<CancellationToken, Task> PrepareSeatVoidCounters(Match match, int voidedCount);

    /// <summary>Guards catalogue deletes: a venue or category in use by a match cannot be removed.</summary>
    Task<bool> ExistsForVenueAsync(Guid venueId, CancellationToken cancellationToken = default);

    Task<bool> ExistsForCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
