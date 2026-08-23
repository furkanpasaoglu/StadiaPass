using MediatR;

namespace StadiaPass.Application.Venues.Commands.DefineVenue;

public sealed record DefineVenueCommand(
    string Name,
    string City,
    string Kind,
    IReadOnlyList<DefineVenueBlock> Blocks) : IRequest<VenueDto>;

public sealed record DefineVenueBlock(string Name, int RowCount, int SeatsPerRow, decimal PriceMultiplier = 1m);
