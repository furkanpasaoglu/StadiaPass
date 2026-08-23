using Microsoft.EntityFrameworkCore;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Persistence.Repositories;

internal sealed class MatchRepository(StadiaPassDbContext context)
    : Repository<Match>(context), IMatchRepository
{
    public async Task<IReadOnlyList<Match>> GetUpcomingAsync(
        DateTimeOffset fromUtc,
        CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(match => match.KickOffUtc >= fromUtc && match.Status != MatchStatus.Cancelled)
            .OrderBy(match => match.KickOffUtc)
            .ToListAsync(cancellationToken);
}
