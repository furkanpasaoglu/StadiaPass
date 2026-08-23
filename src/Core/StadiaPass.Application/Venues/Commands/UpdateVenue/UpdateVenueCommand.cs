using MediatR;
using StadiaPass.Application.Venues.Commands.CreateVenue;

namespace StadiaPass.Application.Venues.Commands.UpdateVenue;

/// <summary>
/// Blocks are optional: pass them only to reshape the seating plan, which is refused once a match has been
/// opened against the venue because those matches already materialised their seats.
/// </summary>
public sealed record UpdateVenueCommand(
    Guid Id,
    string Name,
    string City,
    string Kind,
    IReadOnlyList<VenueBlockInput>? Blocks = null) : IRequest<VenueDto>;
