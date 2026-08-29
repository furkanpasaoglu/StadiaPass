using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Matches.Commands.ReindexMatches;
using StadiaPass.Application.Matches.Search;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.UnitTests.Matches;

/// <summary>
/// PostgreSQL is the source of truth and the index is derived from it. That claim is only true because this
/// command exists, so what it has to get right is the order it works in and the fact that one missing venue
/// cannot take the whole rebuild down with it.
/// </summary>
public sealed class ReindexMatchesCommandHandlerTests
{
    private readonly IMatchSearchIndex _searchIndex = Substitute.For<IMatchSearchIndex>();

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IVenueRepository _venueRepository = Substitute.For<IVenueRepository>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    private readonly ReindexMatchesCommandHandler _handler;

    public ReindexMatchesCommandHandlerTests()
    {
        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        _handler = new ReindexMatchesCommandHandler(
            _searchIndex,
            _matchRepository,
            _venueRepository,
            _dateTimeProvider,
            NullLogger<ReindexMatchesCommandHandler>.Instance);
    }

    [Fact]
    public async Task Should_ThrowTheIndexAwayBeforeWritingToIt_When_TheCatalogueIsRebuilt()
    {
        // Arrange
        var venue = TestData.Stadium();
        GivenTheCatalogue([venue], AFixtureAt(venue, "Fenerbahce"));

        // Act
        await _handler.Handle(new ReindexMatchesCommand(), CancellationToken.None);

        // Assert - the other order writes the documents and then deletes them, and every search afterwards
        // succeeds while finding nothing, which looks exactly like an empty catalogue.
        Received.InOrder(() =>
        {
            _searchIndex.RecreateAsync(Arg.Any<CancellationToken>());
            _searchIndex.IndexAsync(Arg.Any<IReadOnlyCollection<MatchSearchDocument>>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Should_AskForUpcomingFixturesOnly_When_TheCatalogueIsRebuilt()
    {
        // Arrange
        var venue = TestData.Stadium();
        GivenTheCatalogue([venue], AFixtureAt(venue, "Fenerbahce"));

        // Act
        await _handler.Handle(new ReindexMatchesCommand(), CancellationToken.None);

        // Assert - a fixture that has kicked off cannot be bought, so putting it in front of a visitor wastes
        // their click and grows the index for ever. Every category goes in: narrowing happens at query time.
        await _matchRepository.Received(1).GetUpcomingAsync(TestData.Now, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_JoinTheCityOntoTheDocument_When_TheVenueIsKnown()
    {
        // Arrange - the match aggregate denormalises the venue's name but not where it is, so the city has to
        // be looked up or nobody can search by it.
        var venue = TestData.Stadium();
        GivenTheCatalogue([venue], AFixtureAt(venue, "Fenerbahce"));

        // Act
        await _handler.Handle(new ReindexMatchesCommand(), CancellationToken.None);

        // Assert
        CapturedDocuments().Single().City.Should().Be("Istanbul");
    }

    [Fact]
    public async Task Should_StillIndexTheFixture_When_ItsVenueHasBeenDeleted()
    {
        // Arrange - a fixture whose venue is gone is still worth finding by the names of its teams.
        var venue = TestData.Stadium();
        GivenTheCatalogue([], AFixtureAt(venue, "Fenerbahce"));

        // Act
        await _handler.Handle(new ReindexMatchesCommand(), CancellationToken.None);

        // Assert - looking the city up with an indexer rather than a defaulting read would throw here, and
        // one deleted venue would stop the whole catalogue from being rebuilt.
        CapturedDocuments().Single().City.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_ReportHowManyFixturesItWrote()
    {
        // Arrange
        var venue = TestData.Stadium();
        GivenTheCatalogue([venue], AFixtureAt(venue, "Fenerbahce"), AFixtureAt(venue, "Besiktas"));

        // Act
        var result = await _handler.Handle(new ReindexMatchesCommand(), CancellationToken.None);

        // Assert - the number the operator reads back to decide whether the rebuild did anything.
        result.IndexedMatchCount.Should().Be(2);
    }

    /// <summary>A fixture at a venue the test holds, so the join the handler performs has something to find.</summary>
    private static Match AFixtureAt(Venue venue, string homeTeam) =>
        Match.Create(
            TestData.Football(),
            venue,
            homeTeam,
            "Galatasaray",
            TestData.Now.AddDays(30),
            Money.Create(100m),
            TestData.Now);

    private void GivenTheCatalogue(IReadOnlyList<Venue> venues, params Match[] matches)
    {
        _matchRepository
            .GetUpcomingAsync(Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(matches);

        _venueRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(venues);
    }

    private IReadOnlyCollection<MatchSearchDocument> CapturedDocuments()
    {
        var call = _searchIndex
            .ReceivedCalls()
            .Single(received => received.GetMethodInfo().Name == nameof(IMatchSearchIndex.IndexAsync));

        return (IReadOnlyCollection<MatchSearchDocument>)call.GetArguments()[0]!;
    }
}
