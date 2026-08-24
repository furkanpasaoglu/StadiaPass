using StadiaPass.Domain.Common;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Matches.Events;

namespace StadiaPass.Domain.UnitTests.Matches;

/// <summary>
/// The seat lifecycle is the part of the model that stops the same seat being sold twice, so it is asserted
/// from every angle: the happy transition, the hold window, and each way a second buyer can be refused.
/// </summary>
public sealed class MatchSeatReservationTests
{
    private const string OtherHolder = "9c1f2b44-0f4a-4e6b-8a7e-11c8d5b6e3aa";

    [Fact]
    public void Should_MarkSeatReserved_When_AnAvailableSeatIsReserved()
    {
        // Arrange
        var match = TestData.FootballMatch();

        // Act
        var seat = match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Assert
        seat.Status.Should().Be(SeatStatus.Reserved);
        seat.HolderReference.Should().Be(TestData.Holder);
        seat.ReservedAtUtc.Should().Be(TestData.Now);
    }

    [Fact]
    public void Should_HoldTheSeatForTenMinutes_When_AnAvailableSeatIsReserved()
    {
        // Arrange
        var match = TestData.FootballMatch();

        // Act
        var seat = match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Assert - the window is a domain constant, so the expectation is derived from it rather than hard-coded twice.
        Match.ReservationWindow.Should().Be(TimeSpan.FromMinutes(10));
        seat.ReservationExpiresAtUtc.Should().Be(TestData.Now.AddMinutes(10));
        seat.IsReservationExpired(TestData.Now.AddMinutes(9)).Should().BeFalse();
        seat.IsReservationExpired(TestData.Now.AddMinutes(11)).Should().BeTrue();
    }

    [Fact]
    public void Should_MoveTheSeatOutOfTheAvailablePool_When_AnAvailableSeatIsReserved()
    {
        // Arrange
        var match = TestData.FootballMatch();
        var availableBefore = match.AvailableSeatCount;

        // Act
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Assert - the counters a listing screen reads must never drift from the seats themselves.
        match.AvailableSeatCount.Should().Be(availableBefore - 1);
        match.ReservedSeatCount.Should().Be(1);
        match.SoldSeatCount.Should().Be(0);
    }

    [Fact]
    public void Should_RaiseSeatReservedDomainEvent_When_AnAvailableSeatIsReserved()
    {
        // Arrange
        var match = TestData.FootballMatch();

        // Act
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Assert
        match.DomainEvents.OfType<SeatReservedDomainEvent>().Should().ContainSingle()
            .Which.Should().Match<SeatReservedDomainEvent>(reserved =>
                reserved.SeatNumber == "MARATON-1-1"
                && reserved.HolderReference == TestData.Holder
                && reserved.ExpiresAtUtc == TestData.Now.AddMinutes(10));
    }

    [Fact]
    public void Should_ThrowException_When_SeatIsAlreadyReserved()
    {
        // Arrange - somebody else is holding the seat and the hold has not expired.
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act - a second buyer arrives one minute later.
        var act = () => match.ReserveSeat("MARATON-1-1", OtherHolder, TestData.Now.AddMinutes(1));

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Seat.NotAvailable")
            .WithMessage("*Reserved*");
    }

    [Fact]
    public void Should_ThrowException_When_SeatIsAlreadySold()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);
        match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, TestData.Now.AddMinutes(1));

        // Act
        var act = () => match.ReserveSeat("MARATON-1-1", OtherHolder, TestData.Now.AddMinutes(2));

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Seat.NotAvailable")
            .WithMessage("*Sold*");
    }

    [Fact]
    public void Should_LeaveTheCountersUntouched_When_ASecondBuyerIsRefused()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);
        var availableAfterFirstHold = match.AvailableSeatCount;

        // Act
        var act = () => match.ReserveSeat("MARATON-1-1", OtherHolder, TestData.Now.AddMinutes(1));

        // Assert - a rejected attempt must not consume inventory.
        act.Should().Throw<DomainRuleViolationException>();
        match.AvailableSeatCount.Should().Be(availableAfterFirstHold);
        match.ReservedSeatCount.Should().Be(1);
    }

    [Fact]
    public void Should_HandTheSeatToTheNewBuyer_When_ThePreviousHoldHasExpired()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act - eleven minutes later the ten minute hold is stale.
        var seat = match.ReserveSeat("MARATON-1-1", OtherHolder, TestData.Now.AddMinutes(11));

        // Assert
        seat.HolderReference.Should().Be(OtherHolder);
        seat.ReservationExpiresAtUtc.Should().Be(TestData.Now.AddMinutes(21));
        match.ReservedSeatCount.Should().Be(1);
    }

    [Fact]
    public void Should_AllowTheSameHolderToExtendTheirOwnHold_When_TheyReserveAgain()
    {
        // Arrange
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act
        var act = () => match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now.AddMinutes(1));

        // Assert - re-clicking your own seat is not a double booking, but it is still refused because the
        // seat is no longer Available; the caller is expected to buy it rather than hold it twice.
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Seat.NotAvailable");
    }

    [Fact]
    public void Should_ThrowDomainRuleViolation_When_TheSeatDoesNotExist()
    {
        // Arrange
        var match = TestData.FootballMatch();

        // Act
        var act = () => match.ReserveSeat("MARATON-9-9", TestData.Holder, TestData.Now);

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Match.SeatNotFound");
    }

    [Fact]
    public void Should_ThrowDomainRuleViolation_When_HolderReferenceIsMissing()
    {
        // Arrange
        var match = TestData.FootballMatch();

        // Act
        var act = () => match.ReserveSeat("MARATON-1-1", "  ", TestData.Now);

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Match.HolderRequired");
    }
}
