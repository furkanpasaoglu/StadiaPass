using Microsoft.EntityFrameworkCore;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Persistence.Repositories;

internal sealed class VenueRepository(StadiaPassDbContext context)
    : Repository<Venue>(context), IVenueRepository
{
    public async Task<IReadOnlyList<Venue>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Include(venue => venue.Blocks)
            .OrderBy(venue => venue.City)
            .ThenBy(venue => venue.Name)
            .ToListAsync(cancellationToken);

    public Task<Venue?> GetWithBlocksAsync(Guid venueId, CancellationToken cancellationToken = default) =>
        Set.AsNoTracking()
            .Include(venue => venue.Blocks)
            .FirstOrDefaultAsync(venue => venue.Id == venueId, cancellationToken);

    public Task<Venue?> GetTrackedWithBlocksAsync(Guid venueId, CancellationToken cancellationToken = default) =>
        Set.Include(venue => venue.Blocks).FirstOrDefaultAsync(venue => venue.Id == venueId, cancellationToken);

    public Task<bool> ExistsAsync(string name, string city, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(venue => venue.Name == name && venue.City == city, cancellationToken);
}
