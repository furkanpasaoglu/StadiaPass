using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
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
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveAsync(MatchCacheKeys.Upcoming, cancellationToken);

        return match.ToDto();
    }
}
