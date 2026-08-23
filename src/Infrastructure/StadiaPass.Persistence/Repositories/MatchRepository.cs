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
        SportCategory? category = null,
        CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(match => match.KickOffUtc >= fromUtc && match.Status != MatchStatus.Cancelled)
            .Where(match => category == null || match.Category == category)
            .OrderBy(match => match.KickOffUtc)
            .ToListAsync(cancellationToken);

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
