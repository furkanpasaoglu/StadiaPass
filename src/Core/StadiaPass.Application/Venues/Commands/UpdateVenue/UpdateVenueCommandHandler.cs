using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Venues.Commands.UpdateVenue;

internal sealed class UpdateVenueCommandHandler(
    IVenueRepository venueRepository,
    IMatchRepository matchRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateVenueCommand, VenueDto>
{
    public async Task<VenueDto> Handle(UpdateVenueCommand request, CancellationToken cancellationToken)
    {
        var venue = await venueRepository.GetTrackedWithBlocksAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Venue), request.Id);

        venue.Rename(request.Name, request.City);
        venue.ChangeKind(Enum.Parse<VenueKind>(request.Kind, ignoreCase: true));

        if (request.Blocks is { Count: > 0 })
        {
            if (await matchRepository.ExistsForVenueAsync(venue.Id, cancellationToken))
            {
                throw new ConflictException(
                    "The seating plan cannot be reshaped while matches are open against this venue.");
            }

            venue.ReplaceBlocks(request.Blocks.Select(block =>
                new BlockLayout(block.Name, block.RowCount, block.SeatsPerRow, block.PriceMultiplier)));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return venue.ToDto();
    }
}
