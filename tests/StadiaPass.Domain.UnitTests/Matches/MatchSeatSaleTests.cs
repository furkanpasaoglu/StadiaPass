using StadiaPass.Domain.Common;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Domain.UnitTests.Matches;

/// <summary>Turning a hold into a sale: only the holder, only in time, and only once.</summary>
public sealed class MatchSeatSaleTests
{
    private const string OtherHolder = "9c1f2b44-0f4a-4e6b-8a7e-11c8d5b6e3aa";

    [Fact]
    public void Should_MarkSeatSold_When_TheHolderConfirmsInTime()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);
        var soldAt = TestData.Now.AddMinutes(2);

        // Act
        var seat = match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, soldAt);

        // Assert
        seat.Status.Should().Be(SeatStatus.Sold);
        seat.SoldAtUtc.Should().Be(soldAt);
        seat.ReservationExpiresAtUtc.Should().BeNull();
        match.SoldSeatCount.Should().Be(1);
        match.ReservedSeatCount.Should().Be(0);
    }

    [Fact]
    public void Should_ThrowException_When_SomebodyElseTriesToBuyAHeldSeat()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act
        var act = () => match.ConfirmSeatSale("MARATON-1-1", OtherHolder, TestData.Now.AddMinutes(1));

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Seat.ReservedByAnotherHolder");
    }

    [Fact]
    public void Should_ThrowException_When_TheHoldHasExpired()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act - eleven minutes is past the ten minute window.
        var act = () => match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, TestData.Now.AddMinutes(11));

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Seat.ReservationExpired");
    }

    [Fact]
    public void Should_ThrowException_When_TheSeatWasNeverHeld()
    {
        // Arrange
        var match = TestData.FootballMatch();

        // Act
        var act = () => match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, TestData.Now);

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Seat.NotReserved");
    }

    [Fact]
    public void Should_ThrowException_When_TheSameSeatIsSoldTwice()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);
        match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, TestData.Now.AddMinutes(1));

        // Act
        var act = () => match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, TestData.Now.AddMinutes(2));

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Seat.NotReserved");
        match.SoldSeatCount.Should().Be(1);
    }

    [Fact]
    public void Should_MarkTheMatchSoldOut_When_TheLastSeatIsSold()
    {
        // Arrange - a one seat venue makes the boundary explicit.
        var venue = TestData.Stadium(new BlockLayout("MARATON", RowCount: 1, SeatsPerRow: 1));
        var match = TestData.FootballMatch(venue);
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act
        match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, TestData.Now.AddMinutes(1));

        // Assert
        match.Status.Should().Be(MatchStatus.SoldOut);
        match.AvailableSeatCount.Should().Be(0);
    }

    [Fact]
    public void Should_ReturnTheSeatToTheAvailablePool_When_AHoldIsReleased()
    {
        // Arrange
        var match = TestData.FootballMatch();
        var availableBefore = match.AvailableSeatCount;
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act
        match.ReleaseSeat("MARATON-1-1", TestData.Now.AddMinutes(1));

        // Assert
        TestData.SeatOf(match, "MARATON-1-1").Status.Should().Be(SeatStatus.Available);
        TestData.SeatOf(match, "MARATON-1-1").HolderReference.Should().BeNull();
        match.AvailableSeatCount.Should().Be(availableBefore);
        match.ReservedSeatCount.Should().Be(0);
    }
}
