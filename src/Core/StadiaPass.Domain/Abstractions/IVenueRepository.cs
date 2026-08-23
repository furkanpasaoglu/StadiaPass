using StadiaPass.Domain.Venues;

namespace StadiaPass.Domain.Abstractions;

public interface IVenueRepository : IRepository<Venue>
{
    Task<IReadOnlyList<Venue>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Read-only load for queries and match creation.</summary>
    Task<Venue?> GetWithBlocksAsync(Guid venueId, CancellationToken cancellationToken = default);

    /// <summary>Tracked load, so the seating plan can be reshaped and saved.</summary>
    Task<Venue?> GetTrackedWithBlocksAsync(Guid venueId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string name, string city, CancellationToken cancellationToken = default);
}
