using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Application.Matches.Queries.GetUpcomingMatches;

internal sealed class GetUpcomingMatchesQueryHandler(
    IMatchRepository matchRepository,
    ICacheService cacheService,
    IDateTimeProvider dateTimeProvider) : IRequestHandler<GetUpcomingMatchesQuery, IReadOnlyList<MatchDto>>
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);

    public async Task<IReadOnlyList<MatchDto>> Handle(
        GetUpcomingMatchesQuery request,
        CancellationToken cancellationToken)
    {
        var category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();

        var cacheKey = category is null ? MatchCacheKeys.Upcoming : $"{MatchCacheKeys.Upcoming}:{category}";

        if (await cacheService.GetAsync<MatchDto[]>(cacheKey, cancellationToken) is { } cached)
        {
            return cached;
        }

        var matches = await matchRepository.GetUpcomingAsync(dateTimeProvider.UtcNow, category, cancellationToken);
        var result = matches.Select(match => match.ToDto()).ToArray();

        await cacheService.SetAsync(cacheKey, result, CacheDuration, cancellationToken);

        return result;
    }
}
