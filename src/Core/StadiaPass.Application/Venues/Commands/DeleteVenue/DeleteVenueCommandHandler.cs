using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Venues.Commands.DeleteVenue;

internal sealed class DeleteVenueCommandHandler(
    IVenueRepository venueRepository,
    IMatchRepository matchRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<DeleteVenueCommand>
{
    public async Task Handle(DeleteVenueCommand request, CancellationToken cancellationToken)
    {
        var venue = await venueRepository.GetTrackedWithBlocksAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Venue), request.Id);

        if (await matchRepository.ExistsForVenueAsync(venue.Id, cancellationToken))
        {
            throw new ConflictException($"'{venue.Name}' is used by at least one match and cannot be deleted.");
        }

        venueRepository.Remove(venue);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
