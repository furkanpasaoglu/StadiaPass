using StadiaPass.Domain.Matches;

namespace StadiaPass.Domain.Abstractions;

public interface IMatchRepository : IRepository<Match>
{
    Task<IReadOnlyList<Match>> GetUpcomingAsync(
        DateTimeOffset fromUtc,
        string? categoryName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many matches <see cref="GetUpcomingAsync"/> would return, without returning them.
    /// </summary>
    /// <remarks>
    /// For the search index to be measured against. The two have to agree on what "belongs in the index"
    /// means, so this counts the same rows that method selects rather than a filter written out again next
    /// to it - a projection judged against a slightly different question would drift on paper while being
    /// perfectly correct.
    /// </remarks>
    Task<int> CountUpcomingAsync(DateTimeOffset fromUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// The matches behind a list of identifiers, for a caller that already knows which ones it wants.
    /// </summary>
    /// <remarks>
    /// This is the second half of a search: the index answers with identifiers in relevance order and the
    /// rows come from here, so what reaches the screen is what the database says now rather than whatever
    /// was true when the index was last written. Two things are left to the caller. The order is not kept -
    /// <c>IN</c> promises nothing about it - so anyone who cares about relevance has to put the rows back in
    /// the order they were asked for. And an identifier with no row behind it simply does not come back,
    /// which is the right answer for an index that has fallen behind the database.
    /// </remarks>
    Task<IReadOnlyList<Match>> GetByIdsAsync(
        IReadOnlyCollection<Guid> matchIds,
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
    /// Which matches are holding seats whose time has run out. A hold is a promise with a deadline on it;
    /// nothing releases one when the deadline passes unless something goes looking, and until then the seat
    /// is unsellable and the counters disagree with the seat map.
    /// </summary>
    /// <remarks>
    /// Identifiers rather than aggregates, because each of these is released in a unit of work of its own.
    /// Handing back tracked matches invites a caller to release them all through one change tracker, and one
    /// match losing a race would then leave its modified seats in that tracker for the next match's save to
    /// carry along - failing again on a stale token, and taking the rest of the sweep down with it.
    /// </remarks>
    Task<IReadOnlyList<Guid>> GetMatchIdsWithExpiredReservationsAsync(
        DateTimeOffset now,
        int maxMatches,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One match, with only the seats whose hold has run out attached - so releasing them in a 20k venue
    /// touches a handful of rows rather than the whole seat map.
    /// </summary>
    Task<Match?> GetWithExpiredReservationsAsync(
        Guid matchId,
        DateTimeOffset now,
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

    /// <summary>
    /// The fixture with the seats somebody is currently holding, and only those.
    /// </summary>
    /// <remarks>
    /// Cancelling gives held seats back, and the aggregate can only give back what was loaded, so the include
    /// is the thing that makes the cancellation complete rather than an optimisation.
    /// </remarks>
    Task<Match?> GetWithHeldSeatsAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the held seats back into the available column and marks the fixture cancelled, in one statement
    /// against the match row.
    /// </summary>
    /// <remarks>
    /// A cancellation of its own rather than a reuse of <see cref="PrepareSeatReleaseCounters"/>, which turns
    /// a sold-out fixture back into an on-sale one and would undo the very thing this is here to write.
    /// </remarks>
    Func<CancellationToken, Task> PrepareMatchCancellationCounters(Match match, int releasedCount);

    /// <summary>Guards catalogue deletes: a venue or category in use by a match cannot be removed.</summary>
    Task<bool> ExistsForVenueAsync(Guid venueId, CancellationToken cancellationToken = default);

    Task<bool> ExistsForCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
