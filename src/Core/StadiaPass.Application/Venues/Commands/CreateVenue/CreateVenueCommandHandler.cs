using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Venues.Commands.CreateVenue;

internal sealed class CreateVenueCommandHandler(IVenueRepository venueRepository, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateVenueCommand, VenueDto>
{
    public async Task<VenueDto> Handle(CreateVenueCommand request, CancellationToken cancellationToken)
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
