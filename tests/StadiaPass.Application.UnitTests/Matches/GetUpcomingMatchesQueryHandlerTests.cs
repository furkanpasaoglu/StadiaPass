using FluentAssertions;
using NSubstitute;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Matches;
using StadiaPass.Application.Matches.Queries.GetUpcomingMatches;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.UnitTests.Matches;

/// <summary>
/// The listing is cached for fifteen seconds, which is fine, and it used to be cached once per category,
/// which was not. Nothing ever invalidated those: a sale or a new fixture cleared the key for "everything"
/// and left the key for "Football" answering with the counts it had a quarter of a minute ago, so the same
/// page told two different stories depending on which tab was open.
/// </summary>
public sealed class GetUpcomingMatchesQueryHandlerTests
{
    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly ICacheService _cacheService = Substitute.For<ICacheService>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    private readonly GetUpcomingMatchesQueryHandler _handler;

    private MatchDto[]? _cached;

    public GetUpcomingMatchesQueryHandlerTests()
    {
        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        // A cache that actually remembers, so "was the second request served from it" is a real question.
        _cacheService
            .GetAsync<MatchDto[]>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => _cached);

        _cacheService
            .SetAsync(Arg.Any<string>(), Arg.Any<MatchDto[]>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _cached = callInfo.Arg<MatchDto[]>();

                return Task.CompletedTask;
            });

        _handler = new GetUpcomingMatchesQueryHandler(_matchRepository, _cacheService, _dateTimeProvider);
    }

    [Fact]
    public async Task Handle_CachesUnderOneKeyForEveryCategory()
    {
        Stored(TestData.FootballMatch("Fenerbahce"), TestData.BasketballMatch("Anadolu Efes"));

        await _handler.Handle(new GetUpcomingMatchesQuery("Football"), CancellationToken.None);
        await _handler.Handle(new GetUpcomingMatchesQuery("Basketball"), CancellationToken.None);
        await _handler.Handle(new GetUpcomingMatchesQuery(null), CancellationToken.None);

        // One key, so there is one thing to invalidate and nothing left behind to go stale. A key per
        // category would have written three.
        await _cacheService.Received(1).SetAsync(
            MatchCacheKeys.Upcoming, Arg.Any<MatchDto[]>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());

        // And the database was asked once, not once per tab.
        await _matchRepository.Received(1).GetUpcomingAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StillNarrowsToTheCategoryAsked()
    {
        Stored(TestData.FootballMatch("Fenerbahce"), TestData.BasketballMatch("Anadolu Efes"));

        var football = await _handler.Handle(new GetUpcomingMatchesQuery("Football"), CancellationToken.None);
        var everything = await _handler.Handle(new GetUpcomingMatchesQuery(null), CancellationToken.None);

        football.Should().ContainSingle().Which.Category.Should().Be("Football");
        everything.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_TreatsABlankCategoryAsEverything()
    {
        Stored(TestData.FootballMatch("Fenerbahce"), TestData.BasketballMatch("Anadolu Efes"));

        var result = await _handler.Handle(new GetUpcomingMatchesQuery("   "), CancellationToken.None);

        result.Should().HaveCount(2);
    }

    private void Stored(params Match[] matches) =>
        _matchRepository
            .GetUpcomingAsync(Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(matches);
}
