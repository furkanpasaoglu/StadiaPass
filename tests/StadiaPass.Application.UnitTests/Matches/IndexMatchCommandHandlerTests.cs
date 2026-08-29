using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Matches.Commands.IndexMatch;
using StadiaPass.Application.Matches.Search;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.UnitTests.Matches;

/// <summary>
/// The projection that keeps the index level with the database one fixture at a time. It runs on a consumer,
/// which decides both of its interesting behaviours: a message about a fixture that no longer exists is not
/// an error, and an index that would not take the write is not something to forgive.
/// </summary>
public sealed class IndexMatchCommandHandlerTests
{
    private readonly IMatchSearchIndex _searchIndex = Substitute.For<IMatchSearchIndex>();

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IVenueRepository _venueRepository = Substitute.For<IVenueRepository>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    private readonly IndexMatchCommandHandler _handler;

    private readonly Venue _venue = TestData.Stadium();

    private readonly Match _match;

    public IndexMatchCommandHandlerTests()
    {
        _match = Match.Create(
            TestData.Football(),
            _venue,
            "Fenerbahce",
            "Galatasaray",
            TestData.Now.AddDays(30),
            Money.Create(100m),
            TestData.Now);

        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        _handler = new IndexMatchCommandHandler(
            _searchIndex,
            _matchRepository,
            _venueRepository,
            _dateTimeProvider,
            NullLogger<IndexMatchCommandHandler>.Instance);
    }

    [Fact]
    public async Task Should_WriteTheFixtureIntoTheIndex_When_ItStillExists()
    {
        // Arrange
        GivenTheFixtureExists();
        _venueRepository.GetByIdAsync(_venue.Id, Arg.Any<CancellationToken>()).Returns(_venue);

        // Act
        await _handler.Handle(new IndexMatchCommand(_match.Id), CancellationToken.None);

        // Assert - the document has to carry the identifier the search then fetch pattern reads the row back
        // by, and the city nobody can search on unless it is joined in here.
        var document = CapturedDocuments().Single();
        document.Id.Should().Be(_match.Id);
        document.HomeTeam.Should().Be("Fenerbahce");
        document.City.Should().Be("Istanbul");
    }

    [Fact]
    public async Task Should_TakeTheFixtureOutOfTheIndex_When_ItNoLongerExists()
    {
        // Arrange - the message outlived the fixture, which is ordinary rather than exceptional.
        _matchRepository.GetByIdAsync(_match.Id, Arg.Any<CancellationToken>()).Returns((Match?)null);

        // Act
        var indexing = async () => await _handler.Handle(new IndexMatchCommand(_match.Id), CancellationToken.None);

        // Assert - without the guard this dereferences null on a consumer, and a fixture somebody deleted
        // sends its message round the retry policy and into the error queue for nothing. Leaving the document
        // for the next full rebuild is not good enough either: until that runs the fixture is still findable
        // and its link still opens.
        await indexing.Should().NotThrowAsync();
        await _searchIndex
            .DidNotReceive()
            .IndexAsync(Arg.Any<IReadOnlyCollection<MatchSearchDocument>>(), Arg.Any<CancellationToken>());
        await _searchIndex.Received(1).DeleteAsync(_match.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_TakeTheFixtureOutOfTheIndex_When_ItHasBeenCalledOff()
    {
        // Arrange
        GivenTheFixtureExists();
        _match.Cancel(TestData.Now);

        // Act
        await _handler.Handle(new IndexMatchCommand(_match.Id), CancellationToken.None);

        // Assert - the listing filters cancelled fixtures out, so an index that keeps them makes the search
        // box the one place in the application that still sells a match nobody is playing.
        await _searchIndex.Received(1).DeleteAsync(_match.Id, Arg.Any<CancellationToken>());
        await _searchIndex
            .DidNotReceive()
            .IndexAsync(Arg.Any<IReadOnlyCollection<MatchSearchDocument>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_TakeTheFixtureOutOfTheIndex_When_ItsKickOffHasPassed()
    {
        // Arrange - a message that has been sitting in a retry queue, or a rebuild that ran late.
        GivenTheFixtureExists();
        _dateTimeProvider.UtcNow.Returns(TestData.Now.AddDays(31));

        // Act
        await _handler.Handle(new IndexMatchCommand(_match.Id), CancellationToken.None);

        // Assert - what belongs in the index is defined once, by the listing query: still ahead of us and not
        // cancelled. A projection that indexed by a different rule would put fixtures in front of visitors
        // that the listing has already stopped showing them.
        await _searchIndex.Received(1).DeleteAsync(_match.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_StillWriteTheFixture_When_ItsVenueHasBeenDeleted()
    {
        // Arrange
        GivenTheFixtureExists();
        _venueRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Venue?)null);

        // Act
        await _handler.Handle(new IndexMatchCommand(_match.Id), CancellationToken.None);

        // Assert - a fixture is worth finding by the names of its teams even when there is no city to show.
        CapturedDocuments().Single().City.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_LetTheFailureOut_When_TheIndexRefusesTheWrite()
    {
        // Arrange
        GivenTheFixtureExists();
        _venueRepository.GetByIdAsync(_venue.Id, Arg.Any<CancellationToken>()).Returns(_venue);
        _searchIndex
            .IndexAsync(Arg.Any<IReadOnlyCollection<MatchSearchDocument>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the cluster was not there"));

        // Act
        var indexing = async () => await _handler.Handle(new IndexMatchCommand(_match.Id), CancellationToken.None);

        // Assert - catching here would switch off both the broker's redelivery and its error queue, and a
        // cluster that was briefly unreachable would leave an index that quietly stops keeping up. This is the
        // opposite of what the search query does with the same failure, and deliberately so.
        await indexing.Should().ThrowAsync<InvalidOperationException>();
    }

    private void GivenTheFixtureExists() =>
        _matchRepository.GetByIdAsync(_match.Id, Arg.Any<CancellationToken>()).Returns(_match);

    private IReadOnlyCollection<MatchSearchDocument> CapturedDocuments()
    {
        var call = _searchIndex
            .ReceivedCalls()
            .Single(received => received.GetMethodInfo().Name == nameof(IMatchSearchIndex.IndexAsync));

        return (IReadOnlyCollection<MatchSearchDocument>)call.GetArguments()[0]!;
    }
}
