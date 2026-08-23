using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Venues;

public sealed record VenueDto(
    Guid Id,
    string Name,
    string City,
    string Kind,
    int Capacity,
    IReadOnlyList<VenueBlockDto> Blocks);

public sealed record VenueBlockDto(string Name, int RowCount, int SeatsPerRow, decimal PriceMultiplier, int Capacity);

internal static class VenueMappings
{
    public static VenueDto ToDto(this Venue venue) => new(
        venue.Id,
        venue.Name,
        venue.City,
        venue.Kind.ToString(),
        venue.Capacity,
        [.. venue.Blocks.Select(block => new VenueBlockDto(
            block.Name,
            block.RowCount,
            block.SeatsPerRow,
            block.PriceMultiplier,
            block.Capacity))]);
}
