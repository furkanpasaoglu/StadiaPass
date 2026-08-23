using StadiaPass.Domain.Matches;

namespace StadiaPass.Domain.Abstractions;

public interface IMatchRepository : IRepository<Match>
{
    Task<IReadOnlyList<Match>> GetUpcomingAsync(DateTimeOffset fromUtc, CancellationToken cancellationToken = default);
}
