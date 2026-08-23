using StadiaPass.Domain.Venues;

namespace StadiaPass.Domain.Abstractions;

public interface IVenueRepository : IRepository<Venue>
{
    Task<IReadOnlyList<Venue>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Venue?> GetWithBlocksAsync(Guid venueId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string name, string city, CancellationToken cancellationToken = default);
}
