using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Venues.Commands.DefineVenue;

internal sealed class DefineVenueCommandHandler(IVenueRepository venueRepository, IUnitOfWork unitOfWork)
    : IRequestHandler<DefineVenueCommand, VenueDto>
{
    public async Task<VenueDto> Handle(DefineVenueCommand request, CancellationToken cancellationToken)
    {
        if (await venueRepository.ExistsAsync(request.Name, request.City, cancellationToken))
        {
            throw new ConflictException($"Venue '{request.Name}' in {request.City} is already defined.");
        }

        var venue = Venue.Define(
            request.Name,
            request.City,
            Enum.Parse<VenueKind>(request.Kind, ignoreCase: true),
            request.Blocks.Select(block =>
                new BlockLayout(block.Name, block.RowCount, block.SeatsPerRow, block.PriceMultiplier)));

        await venueRepository.AddAsync(venue, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return venue.ToDto();
    }
}
