using Microsoft.EntityFrameworkCore;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Persistence.Repositories;

internal sealed class MatchRepository(StadiaPassDbContext context)
    : Repository<Match>(context), IMatchRepository
{
    public async Task<IReadOnlyList<Match>> GetUpcomingAsync(
        DateTimeOffset fromUtc,
        string? categoryName = null,
        CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(match => match.KickOffUtc >= fromUtc && match.Status != MatchStatus.Cancelled)
            .Where(match => categoryName == null || match.CategoryName == categoryName)
            .OrderBy(match => match.KickOffUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Func<CancellationToken, Task> PrepareSeatSaleCounters(Match match)
    {
        // The aggregate has already moved its own counters in memory, and it should: that is what keeps the
        // domain rules - and the tests that pin them down - honest. Those values are simply not what belongs
        // in the database, so they are taken out of the save's hands here and left to the statement below.
        var entry = Context.Entry(match);
        entry.Property(candidate => candidate.ReservedSeatCount).IsModified = false;
        entry.Property(candidate => candidate.SoldSeatCount).IsModified = false;
        entry.Property(candidate => candidate.Status).IsModified = false;

        // Every SET expression reads the row as it was before this statement, which is why the sold-out test
        // asks whether the reservation being sold is the last one rather than whether the count is already
        // zero. PostgreSQL re-evaluates all of it against the committed row if another sale got here first,
        // so the arithmetic stays right without a version check and without anybody losing an update.
        return cancellationToken => Set
            .Where(candidate => candidate.Id == match.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.ReservedSeatCount, candidate => candidate.ReservedSeatCount - 1)
                    .SetProperty(candidate => candidate.SoldSeatCount, candidate => candidate.SoldSeatCount + 1)
                    .SetProperty(
                        candidate => candidate.Status,
                        candidate => candidate.AvailableSeatCount == 0 && candidate.ReservedSeatCount == 1
                            ? MatchStatus.SoldOut
                            : candidate.Status),
                cancellationToken);
    }

    /// <inheritdoc />
    public Func<CancellationToken, Task> PrepareSeatReservationCounters(Match match, int reservedCount)
    {
        if (reservedCount is 0)
        {
            // An expired hold changing hands. The seat row still has to be written, and the counters are
            // already right, so there is nothing here to hand to the database - and no reason to take the
            // match row at all.
            return static _ => Task.CompletedTask;
        }

        var entry = Context.Entry(match);
        entry.Property(candidate => candidate.AvailableSeatCount).IsModified = false;
        entry.Property(candidate => candidate.ReservedSeatCount).IsModified = false;

        // No status in the SET list, and none taken out of the save either: reserving never opens or closes
        // a match. A fixture whose every seat is held is not sold out - a hold is a promise with a deadline
        // on it, and the ones nobody keeps come back.
        return cancellationToken => Set
            .Where(candidate => candidate.Id == match.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        candidate => candidate.AvailableSeatCount,
                        candidate => candidate.AvailableSeatCount - reservedCount)
                    .SetProperty(
                        candidate => candidate.ReservedSeatCount,
                        candidate => candidate.ReservedSeatCount + reservedCount),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetMatchIdsWithExpiredReservationsAsync(
        DateTimeOffset now,
        int maxMatches,
        CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(match => match.Seats.Any(seat =>
                seat.Status == SeatStatus.Reserved && seat.ReservationExpiresAtUtc < now))
            .OrderBy(match => match.KickOffUtc)
            .Take(maxMatches)
            .Select(match => match.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Match?> GetWithExpiredReservationsAsync(
        Guid matchId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        // Filtered include: a match in a 20k venue comes back carrying only the handful of seats whose hold
        // has run out, not its whole seat map.
        Set
            .Include(match => match.Seats.Where(seat =>
                seat.Status == SeatStatus.Reserved && seat.ReservationExpiresAtUtc < now))
            .FirstOrDefaultAsync(match => match.Id == matchId, cancellationToken);

    /// <inheritdoc />
    public Func<CancellationToken, Task> PrepareSeatReleaseCounters(Match match, int releasedCount)
    {
        var entry = Context.Entry(match);
        entry.Property(candidate => candidate.ReservedSeatCount).IsModified = false;
        entry.Property(candidate => candidate.AvailableSeatCount).IsModified = false;
        entry.Property(candidate => candidate.Status).IsModified = false;

        // The mirror image of a sale: seats move back from reserved to available, and a match that had sold
        // out has not any more. Whether it was sold out is asked of the row rather than of the counters,
        // because those are the values this request happened to read and something else may have moved them.
        return cancellationToken => Set
            .Where(candidate => candidate.Id == match.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        candidate => candidate.ReservedSeatCount,
                        candidate => candidate.ReservedSeatCount - releasedCount)
                    .SetProperty(
                        candidate => candidate.AvailableSeatCount,
                        candidate => candidate.AvailableSeatCount + releasedCount)
                    .SetProperty(
                        candidate => candidate.Status,
                        candidate => candidate.Status == MatchStatus.SoldOut
                            ? MatchStatus.OnSale
                            : candidate.Status),
                cancellationToken);
    }

    /// <inheritdoc />
    public Func<CancellationToken, Task> PrepareSeatVoidCounters(Match match, int voidedCount)
    {
        var entry = Context.Entry(match);
        entry.Property(candidate => candidate.SoldSeatCount).IsModified = false;
        entry.Property(candidate => candidate.AvailableSeatCount).IsModified = false;
        entry.Property(candidate => candidate.Status).IsModified = false;

        // A sale taken back: the seat leaves the sold count and rejoins the available one, and a match that
        // had sold out is on sale again. Asked of the row rather than of the counters this request happened
        // to read, for the same reason every other one of these is.
        return cancellationToken => Set
            .Where(candidate => candidate.Id == match.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        candidate => candidate.SoldSeatCount,
                        candidate => candidate.SoldSeatCount - voidedCount)
                    .SetProperty(
                        candidate => candidate.AvailableSeatCount,
                        candidate => candidate.AvailableSeatCount + voidedCount)
                    .SetProperty(
                        candidate => candidate.Status,
                        candidate => candidate.Status == MatchStatus.SoldOut
                            ? MatchStatus.OnSale
                            : candidate.Status),
                cancellationToken);
    }

    public Task<bool> ExistsForVenueAsync(Guid venueId, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(match => match.VenueId == venueId, cancellationToken);

    public Task<bool> ExistsForCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(match => match.CategoryId == categoryId, cancellationToken);

    public Task<Match?> GetWithSeatMapAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        Set.AsNoTracking()
            .Include(match => match.Seats)
            .AsSplitQuery()
            .FirstOrDefaultAsync(match => match.Id == matchId, cancellationToken);

    /// <summary>
    /// Filtered include: only the requested seat is materialised, so reserving a seat in a 20k venue still
    /// touches a single row instead of loading the whole map.
    /// </summary>
    public Task<Match?> GetWithSeatAsync(
        Guid matchId,
        string seatNumber,
        CancellationToken cancellationToken = default)
    {
        var seat = SeatNumber.Parse(seatNumber);

        return Set
            .Include(match => match.Seats.Where(candidate => candidate.SeatNumber == seat))
            .FirstOrDefaultAsync(match => match.Id == matchId, cancellationToken);
    }
}
