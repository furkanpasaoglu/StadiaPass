using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Matches.Events;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Categories;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Matches.Commands.CreateMatch;

internal sealed class CreateMatchCommandHandler(
    IMatchRepository matchRepository,
    IVenueRepository venueRepository,
    ISportCategoryRepository categoryRepository,
    ICacheService cacheService,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : IRequestHandler<CreateMatchCommand, MatchDto>
{
    public async Task<MatchDto> Handle(CreateMatchCommand request, CancellationToken cancellationToken)
    {
        var venue = await venueRepository.GetWithBlocksAsync(request.VenueId, cancellationToken)
            ?? throw new NotFoundException(nameof(Venue), request.VenueId);

        var category = await categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken)
            ?? throw new NotFoundException(nameof(SportCategory), request.CategoryId);

        var match = Match.Create(
            category,
            venue,
            request.HomeTeam,
            request.AwayTeam,
            request.KickOffUtc,
            Money.Create(request.BasePrice, request.Currency),
            dateTimeProvider.UtcNow);

        await matchRepository.AddAsync(match, cancellationToken);

        // Staged before the save, so the row that tells the search index about this fixture is written by the
        // same SaveChanges as the fixture itself. Publishing afterwards would let the two disagree: a match
        // nobody can find, or a message about a match that rolled back.
        outbox.Enqueue(new MatchCatalogueChangedEvent(match.Id));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveAsync(MatchCacheKeys.Upcoming, cancellationToken);

        return match.ToDto();
    }
}
