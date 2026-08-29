using StadiaPass.Domain.Common;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Matches.Events;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Domain.UnitTests.Matches;

/// <summary>
/// Calling a fixture off. The aggregate does the two things that have to be true the instant an official
/// says so - nothing more may be sold, and nobody is left holding a seat - and deliberately does not touch
/// the seats that were actually paid for.
/// </summary>
/// <remarks>
/// Sold seats are settled one ticket at a time, off the broker, because each one owes somebody money and a
/// refund that fails has to be able to be retried on its own rather than taking a whole fixture's worth of
/// them down with it. What the aggregate guarantees here is only that the till is shut.
/// </remarks>
public sealed class MatchCancellationTests
{
    [Fact]
    public void Should_ShutTheTill_When_TheMatchIsCancelled()
    {
        // Arrange
        var match = TestData.FootballMatch();

        // Act
        match.Cancel(TestData.Now);

        // Assert - the sales guard already refuses anything that is not OnSale, so this one assignment is
        // what stops holds and purchases alike.
        match.Status.Should().Be(MatchStatus.Cancelled);
        var reserving = () => match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);
        reserving.Should().Throw<DomainRuleViolationException>().Which.Rule.Should().Be("Match.SalesClosed");
    }

    [Fact]
    public void Should_GiveBackEveryHeldSeat_When_TheMatchIsCancelled()
    {
        // Arrange - two people mid-checkout when the fixture is called off.
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);
        match.ReserveSeat("MARATON-1-2", "9c1f2b44-0f4a-4e6b-8a7e-11c8d5b6e3aa", TestData.Now);

        // Act
        match.Cancel(TestData.Now);

        // Assert - a hold left behind is a seat counted against a fixture nobody can buy from, and the
        // sweeper would not touch it for another ten minutes.
        TestData.SeatOf(match, "MARATON-1-1").Status.Should().Be(SeatStatus.Available);
        TestData.SeatOf(match, "MARATON-1-2").Status.Should().Be(SeatStatus.Available);
        match.ReservedSeatCount.Should().Be(0);
        match.AvailableSeatCount.Should().Be(match.Capacity);
    }

    [Fact]
    public void Should_LeaveSoldSeatsAsTheyAre_When_TheMatchIsCancelled()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);
        match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act
        match.Cancel(TestData.Now);

        // Assert - each of these owes somebody money, so they are settled ticket by ticket off the broker.
        // Freeing them here would lose the only record of what still has to be paid back.
        TestData.SeatOf(match, "MARATON-1-1").Status.Should().Be(SeatStatus.Sold);
        match.SoldSeatCount.Should().Be(1);
    }

    [Fact]
    public void Should_AnnounceTheCancellation()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ClearDomainEvents();

        // Act
        match.Cancel(TestData.Now);

        // Assert - raised for the sake of the model rather than for a listener. Nothing subscribes to it:
        // the refunds hang off the outbox message the command stages, the search index off the catalogue
        // message beside it, and the cached listing off a call in the command itself. It is asserted because
        // an aggregate that changes this much and announces nothing is the thing worth noticing.
        match.DomainEvents.OfType<MatchCancelledDomainEvent>().Should().ContainSingle()
            .Which.MatchId.Should().Be(match.Id);
    }

    [Fact]
    public void Should_RefuseASecondCancellation()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.Cancel(TestData.Now);

        // Act
        var cancellingAgain = () => match.Cancel(TestData.Now);

        // Assert - the second one would announce a second round of refunds for tickets the first round has
        // already paid back.
        cancellingAgain.Should().Throw<DomainRuleViolationException>()
            .Which.Rule.Should().Be("Match.AlreadyCancelled");
    }

    [Fact]
    public void Should_RefuseToCancelAMatchThatHasAlreadyKickedOff()
    {
        // Arrange
        var match = TestData.FootballMatch();

        // Act
        var cancelling = () => match.Cancel(TestData.KickOff.AddMinutes(1));

        // Assert - a fixture that has been played cannot be called off, and treating it as though it could
        // would refund every ticket for a match the crowd actually watched.
        cancelling.Should().Throw<DomainRuleViolationException>()
            .Which.Rule.Should().Be("Match.AlreadyKickedOff");
    }

    [Fact]
    public void Should_StayCancelled_When_ASoldOutFixtureGivesASeatBackAfterwards()
    {
        // Arrange - every seat sold, then the fixture called off, then one of those sales taken back the way
        // a chargeback would take it.
        var match = TestData.FootballMatch(TestData.Stadium(new BlockLayout("MARATON", RowCount: 1, SeatsPerRow: 1)));
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);
        match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, TestData.Now);
        match.Status.Should().Be(MatchStatus.SoldOut);

        match.Cancel(TestData.Now);

        // Act
        match.VoidSeatSale("MARATON-1-1", TestData.Now);

        // Assert - taking a seat back puts a sold-out fixture on sale again, and a cancelled one must not be
        // resurrected by the same line. This is the shape the whole settlement runs in: every refunded
        // ticket voids its seat on a fixture that has already been called off.
        match.Status.Should().Be(MatchStatus.Cancelled);
    }
}
