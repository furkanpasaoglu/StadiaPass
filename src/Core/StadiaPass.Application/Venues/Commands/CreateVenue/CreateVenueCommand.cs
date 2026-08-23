using MediatR;

namespace StadiaPass.Application.Venues.Commands.CreateVenue;

public sealed record CreateVenueCommand(
    string Name,
    string City,
    string Kind,
    IReadOnlyList<VenueBlockInput> Blocks) : IRequest<VenueDto>;

public sealed record VenueBlockInput(string Name, int RowCount, int SeatsPerRow, decimal PriceMultiplier = 1m);
