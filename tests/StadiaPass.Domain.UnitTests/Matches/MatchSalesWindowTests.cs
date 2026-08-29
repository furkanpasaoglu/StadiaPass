using StadiaPass.Domain.Common;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Domain.UnitTests.Matches;

/// <summary>
/// Selling stops when the match starts. Nothing said so before: the listing hides a fixture once its
/// kick-off has passed, but the seat map is fetched by identifier and does not filter by date, so anybody
/// holding a link to last week's fixture - a bookmark, a shared URL, a document left in the search index -
/// could open it, take a hold and pay for a seat at a match that had already been played.
/// </summary>
/// <remarks>
/// The two ways back out of a seat deliberately keep working afterwards, and are asserted here so that
/// closing the door does not wall them in: a chargeback arrives long after the sale that caused it, and the
/// sweeper that gives back abandoned holds has to be able to tidy up a fixture that has since started.
/// </remarks>
public sealed class MatchSalesWindowTests
{
    private static readonly DateTimeOffset AfterKickOff = TestData.KickOff.AddMinutes(1);

    [Fact]
    public void Should_RefuseAHold_When_TheMatchHasAlreadyKickedOff()
    {
        // Arrange
        var match = TestData.FootballMatch();

        // Act
        var reserving = () => match.ReserveSeat("MARATON-1-1", TestData.Holder, AfterKickOff);

        // Assert
        reserving.Should().Throw<DomainRuleViolationException>().Which.Rule.Should().Be("Match.SalesClosed");
    }

    [Fact]
    public void Should_RefuseAHold_When_TheMatchIsKickingOffThisVeryMoment()
    {
        // Arrange - the boundary belongs to the match, not to the box office.
        var match = TestData.FootballMatch();

        // Act
        var reserving = () => match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.KickOff);

        // Assert
        reserving.Should().Throw<DomainRuleViolationException>().Which.Rule.Should().Be("Match.SalesClosed");
    }

    [Fact]
    public void Should_RefuseToTakeMoney_When_TheMatchHasAlreadyKickedOff()
    {
        // Arrange - a seat held before kick-off, reached again afterwards. This is the check the purchase
        // runs before it charges anything, so refusing here is what keeps the card untouched.
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act
        var selling = () => match.EnsureSeatCanBeSoldTo("MARATON-1-1", TestData.Holder, AfterKickOff);

        // Assert
        selling.Should().Throw<DomainRuleViolationException>().Which.Rule.Should().Be("Match.SalesClosed");
    }

    [Fact]
    public void Should_RefuseToCompleteASale_When_TheMatchHasAlreadyKickedOff()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act
        var confirming = () => match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, AfterKickOff);

        // Assert
        confirming.Should().Throw<DomainRuleViolationException>().Which.Rule.Should().Be("Match.SalesClosed");
    }

    [Fact]
    public void Should_StillTakeASaleBack_When_TheMatchHasAlreadyKickedOff()
    {
        // Arrange - a chargeback or a refund raised from the provider's dashboard arrives long after the
        // request that made the sale, and often after the match itself.
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);
        match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act
        var seat = match.VoidSeatSale("MARATON-1-1", AfterKickOff);

        // Assert - refusing this would leave the money returned and the seat still marked sold, which is the
        // worst of both.
        seat.Status.Should().Be(SeatStatus.Available);
        match.SoldSeatCount.Should().Be(0);
    }

    [Fact]
    public void Should_StillGiveBackAnAbandonedHold_When_TheMatchHasAlreadyKickedOff()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act
        match.ReleaseSeat("MARATON-1-1", AfterKickOff);

        // Assert - the sweeper runs on a timer, not on the fixture list, so it will meet started matches.
        TestData.SeatOf(match, "MARATON-1-1").Status.Should().Be(SeatStatus.Available);
        match.ReservedSeatCount.Should().Be(0);
        match.AvailableSeatCount.Should().Be(match.Capacity);
    }
}
