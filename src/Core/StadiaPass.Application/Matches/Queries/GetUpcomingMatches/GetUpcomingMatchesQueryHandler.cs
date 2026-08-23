using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Application.Matches.Queries.GetUpcomingMatches;

internal sealed class GetUpcomingMatchesQueryHandler(
    IMatchRepository matchRepository,
    ICacheService cacheService,
    IDateTimeProvider dateTimeProvider) : IRequestHandler<GetUpcomingMatchesQuery, IReadOnlyList<MatchDto>>
{
    private const string CacheKey = "matches:upcoming";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<MatchDto>> Handle(
        GetUpcomingMatchesQuery request,
        CancellationToken cancellationToken)
    {
        if (await cacheService.GetAsync<MatchDto[]>(CacheKey, cancellationToken) is { } cached)
        {
            return cached;
        }

        var matches = await matchRepository.GetUpcomingAsync(dateTimeProvider.UtcNow, cancellationToken);
        var result = matches.Select(match => match.ToDto()).ToArray();

        await cacheService.SetAsync(CacheKey, result, CacheDuration, cancellationToken);

        return result;
    }
}
