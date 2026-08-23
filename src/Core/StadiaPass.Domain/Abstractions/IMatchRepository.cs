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

    /// <summary>Guards catalogue deletes: a venue or category in use by a match cannot be removed.</summary>
    Task<bool> ExistsForVenueAsync(Guid venueId, CancellationToken cancellationToken = default);

    Task<bool> ExistsForCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
