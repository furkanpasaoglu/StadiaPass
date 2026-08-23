using MediatR;

namespace StadiaPass.Application.Venues.Queries.GetVenues;

public sealed record GetVenuesQuery : IRequest<IReadOnlyList<VenueDto>>;
