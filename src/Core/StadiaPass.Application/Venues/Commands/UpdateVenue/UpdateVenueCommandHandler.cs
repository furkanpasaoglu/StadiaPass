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

        var kind = Enum.Parse<VenueKind>(request.Kind, ignoreCase: true);
        var blocks = request.Blocks is { Count: > 0 } ? request.Blocks : null;

        if ((blocks is not null || kind != venue.Kind)
            && await matchRepository.ExistsForVenueAsync(venue.Id, cancellationToken))
        {
            throw new ConflictException(
                "The seating plan and the kind of building cannot be changed while matches are open against "
                + "this venue.");
        }

        venue.Rename(request.Name, request.City);
        venue.ChangeKind(kind);

        if (blocks is not null)
        {
            venue.ReplaceBlocks(blocks.Select(block =>
                new BlockLayout(block.Name, block.RowCount, block.SeatsPerRow, block.PriceMultiplier)));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return venue.ToDto();
    }
}
