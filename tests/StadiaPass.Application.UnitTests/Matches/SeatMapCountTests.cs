using FluentAssertions;
using NSubstitute;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Matches.Queries.GetMatchSeatMap;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.UnitTests.Matches;

/// <summary>
/// A seat map has to agree with itself. The number above it, the number on each block and the colour of each
/// seat are three ways of saying the same thing to the same person at the same moment.
/// </summary>
public sealed class SeatMapCountTests
{
    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    private readonly GetMatchSeatMapQueryHandler _handler;

    public SeatMapCountTests()
    {
        _handler = new GetMatchSeatMapQueryHandler(_matchRepository, _dateTimeProvider);
    }

    [Fact]
    public async Task Handle_CountsALapsedHoldAsFreeEverywhereOnThePage()
    {
        // Arrange - one seat held, and then long enough passes that the hold is worthless. The sweeper has
        // not been round yet, which is the ordinary case for up to a minute.
        var match = TestData.FootballMatch();
        match.ReserveSeat(TestData.SeatNumber, TestData.CurrentUserId, TestData.Now);

        var afterTheHoldLapsed = TestData.Now + Match.ReservationWindow + TimeSpan.FromMinutes(1);

        _dateTimeProvider.UtcNow.Returns(afterTheHoldLapsed);
        _matchRepository.GetWithSeatMapAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);

        // Act
        var map = await _handler.Handle(new GetMatchSeatMapQuery(match.Id), CancellationToken.None);

        // Assert - the counters still call that seat reserved, so reading the headline from them used to
        // print one fewer free seat than the map underneath it drew in white.
        match.AvailableSeatCount.Should().Be(match.Capacity - 1);

        var seat = map.Blocks.SelectMany(block => block.Rows).SelectMany(row => row.Seats)
            .Single(candidate => candidate.SeatNumber == TestData.SeatNumber);

        seat.Status.Should().Be(nameof(SeatStatus.Available));
        map.AvailableSeatCount.Should().Be(match.Capacity);
        map.Blocks.Sum(block => block.AvailableSeatCount).Should().Be(map.AvailableSeatCount);
    }

    [Fact]
    public async Task Handle_LeavesALiveHoldOutOfTheFreeCount()
    {
        var match = TestData.FootballMatch();
        match.ReserveSeat(TestData.SeatNumber, TestData.CurrentUserId, TestData.Now);

        _dateTimeProvider.UtcNow.Returns(TestData.Now);
        _matchRepository.GetWithSeatMapAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);

        var map = await _handler.Handle(new GetMatchSeatMapQuery(match.Id), CancellationToken.None);

        // Somebody else is paying for it right now, so it is not free to anybody - and here the counters and
        // the seats say the same thing.
        map.AvailableSeatCount.Should().Be(match.Capacity - 1);
        map.Blocks.Sum(block => block.AvailableSeatCount).Should().Be(map.AvailableSeatCount);
    }
}
