using MediatR;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Application.Venues.Queries.GetVenues;

internal sealed class GetVenuesQueryHandler(IVenueRepository venueRepository)
    : IRequestHandler<GetVenuesQuery, IReadOnlyList<VenueDto>>
{
    public async Task<IReadOnlyList<VenueDto>> Handle(GetVenuesQuery request, CancellationToken cancellationToken)
    {
        var venues = await venueRepository.GetAllAsync(cancellationToken);

        return [.. venues.Select(venue => venue.ToDto())];
    }
}
