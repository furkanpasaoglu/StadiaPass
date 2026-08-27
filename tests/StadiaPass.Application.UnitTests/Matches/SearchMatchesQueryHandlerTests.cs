using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Matches.Queries.SearchMatches;
using StadiaPass.Application.Matches.Search;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.UnitTests.Matches;

/// <summary>
/// Search then fetch has three things in it worth pinning down, and all three are quiet when they break:
/// the order the index chose has to survive a database that does not keep it, an identifier the database
/// has nothing for has to disappear rather than throw, and a cluster that is down has to cost the visitor
/// the search and nothing else.
/// </summary>
public sealed class SearchMatchesQueryHandlerTests
{
    private readonly IMatchSearchIndex _searchIndex = Substitute.For<IMatchSearchIndex>();

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    private readonly SearchMatchesQueryHandler _handler;

    public SearchMatchesQueryHandlerTests()
    {
        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        _handler = new SearchMatchesQueryHandler(
            _searchIndex,
            _matchRepository,
            _dateTimeProvider,
            NullLogger<SearchMatchesQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReturnsMatchesInTheOrderTheIndexChose()
    {
        // Handed back deliberately reversed: relevance is the one thing the database cannot recover, so a
        // handler that simply returned what the repository answered would pass every other assertion here
        // and still put the least relevant fixture at the top of the page.
        var (first, second) = (Football("Fenerbahce"), Football("Besiktas"));

        Indexed(second.Id, first.Id);
        Stored(first, second);

        var result = await _handler.Handle(new SearchMatchesQuery("fenerbahce"), CancellationToken.None);

        result.SearchAvailable.Should().BeTrue();
        result.Matches.Select(match => match.Id).Should().Equal(second.Id, first.Id);
    }

    [Fact]
    public async Task Handle_DropsIdentifiersTheDatabaseHasNoRowFor()
    {
        // An index that still remembers a match somebody deleted is behind, not broken.
        var surviving = Football("Fenerbahce");

        Indexed(Guid.CreateVersion7(), surviving.Id);
        Stored(surviving);

        var result = await _handler.Handle(new SearchMatchesQuery("fenerbahce"), CancellationToken.None);

        result.SearchAvailable.Should().BeTrue();
        result.Matches.Should().ContainSingle().Which.Id.Should().Be(surviving.Id);
    }

    [Fact]
    public async Task Handle_FallsBackToTheListingWhenTheIndexCannotBeReached()
    {
        var listed = Football("Fenerbahce");

        _searchIndex
            .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the cluster is down"));

        _matchRepository
            .GetUpcomingAsync(TestData.Now, null, Arg.Any<CancellationToken>())
            .Returns([listed]);

        var result = await _handler.Handle(new SearchMatchesQuery("fenerbahce"), CancellationToken.None);

        // The listing, and the flag that stops the page from calling it an answer.
        result.SearchAvailable.Should().BeFalse();
        result.Matches.Should().ContainSingle().Which.Id.Should().Be(listed.Id);
    }

    [Fact]
    public async Task Handle_ListsEverythingWithoutTouchingTheIndexWhenNothingWasTyped()
    {
        _matchRepository
            .GetUpcomingAsync(TestData.Now, null, Arg.Any<CancellationToken>())
            .Returns([Football("Fenerbahce")]);

        var result = await _handler.Handle(new SearchMatchesQuery("   "), CancellationToken.None);

        result.SearchAvailable.Should().BeTrue();
        result.Matches.Should().ContainSingle();

        // An empty box is not a search that found everything: running it would spend a round trip on a
        // query with nothing in it.
        await _searchIndex.DidNotReceive()
            .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static Match Football(string homeTeam) => TestData.FootballMatch(homeTeam);

    private void Indexed(params Guid[] matchIds) =>
        _searchIndex
            .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(matchIds);

    /// <summary>
    /// Answers the way PostgreSQL does: everything that was asked for, in an order of its own choosing.
    /// </summary>
    private void Stored(params Match[] matches) =>
        _matchRepository
            .GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var asked = callInfo.Arg<IReadOnlyCollection<Guid>>();

                return matches.Where(match => asked.Contains(match.Id)).ToArray();
            });
}
