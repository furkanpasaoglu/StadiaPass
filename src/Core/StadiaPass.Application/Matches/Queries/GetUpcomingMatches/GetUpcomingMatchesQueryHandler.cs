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

        var all = await GetAllAsync(cancellationToken);

        return category is null
            ? all
            : [.. all.Where(match => string.Equals(match.Category, category, StringComparison.Ordinal))];
    }

    /// <summary>
    /// The whole listing under one key, with the category picked out of it afterwards.
    /// </summary>
    /// <remarks>
    /// It used to be a key per category, and nothing ever invalidated those. A sale or a new fixture cleared
    /// <c>matches:upcoming</c> and left <c>matches:upcoming:Football</c> answering with the counts it had a
    /// quarter of a minute ago, so the same page told two different stories depending on which tab was open.
    /// Filtering in memory deletes the key that could go stale rather than adding somewhere else to remember
    /// to clear it, and costs nothing: the All tab already reads every upcoming match, and this is that
    /// list read again.
    /// </remarks>
    private async Task<MatchDto[]> GetAllAsync(CancellationToken cancellationToken)
    {
        if (await cacheService.GetAsync<MatchDto[]>(MatchCacheKeys.Upcoming, cancellationToken) is { } cached)
        {
            return cached;
        }

        var matches = await matchRepository.GetUpcomingAsync(
            dateTimeProvider.UtcNow, categoryName: null, cancellationToken);

        var result = matches.Select(match => match.ToDto()).ToArray();

        await cacheService.SetAsync(MatchCacheKeys.Upcoming, result, CacheDuration, cancellationToken);

        return result;
    }
}
