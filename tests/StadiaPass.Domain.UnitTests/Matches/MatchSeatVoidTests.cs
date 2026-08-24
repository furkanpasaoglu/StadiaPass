using StadiaPass.Domain.Common;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Matches.Events;

namespace StadiaPass.Domain.UnitTests.Matches;

/// <summary>
/// Voiding a sale is the only way a seat ever leaves <see cref="SeatStatus.Sold"/>. It exists because money
/// can be taken back long after the request that took it - a chargeback, or a refund issued from the
/// provider's own dashboard - and a seat left sold after that is a seat nobody has paid for and nobody can
/// buy. These pin down that it gives the seat back completely, corrects the counters, and refuses anything
/// that was not a sale in the first place.
/// </summary>
public sealed class MatchSeatVoidTests
{
    [Fact]
    public void Should_PutTheSeatBackOnOffer_When_ASaleIsVoided()
    {
        // Arrange
        var match = SoldMatch(out var seatNumber);

        // Act
        var seat = match.VoidSeatSale(seatNumber, TestData.Now);

        // Assert - nothing of the sale is left behind, or the seat map would show a stranger's name on a
        // seat that is for sale.
        seat.Status.Should().Be(SeatStatus.Available);
        seat.HolderReference.Should().BeNull();
        seat.SoldAtUtc.Should().BeNull();
        seat.ReservedAtUtc.Should().BeNull();
        seat.ReservationExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public void Should_MoveTheSeatFromSoldBackToAvailable_When_ASaleIsVoided()
    {
        // Arrange
        var match = SoldMatch(out var seatNumber);
        var sold = match.SoldSeatCount;
        var available = match.AvailableSeatCount;

        // Act
        match.VoidSeatSale(seatNumber, TestData.Now);

        // Assert
        match.SoldSeatCount.Should().Be(sold - 1);
        match.AvailableSeatCount.Should().Be(available + 1);
    }

    [Fact]
    public void Should_PutTheMatchBackOnSale_When_TheVoidedSeatWasTheLastOne()
    {
        // Arrange - sell the lot, so the match is genuinely sold out rather than told that it is.
        var match = TestData.FootballMatch();
        var seatNumbers = match.Seats.Select(seat => seat.SeatNumber.ToString()).ToArray();

        foreach (var seatNumber in seatNumbers)
        {
            match.ReserveSeat(seatNumber, TestData.Holder, TestData.Now);
            match.ConfirmSeatSale(seatNumber, TestData.Holder, TestData.Now);
        }

        match.Status.Should().Be(MatchStatus.SoldOut);

        // Act
        match.VoidSeatSale(seatNumbers[0], TestData.Now);

        // Assert
        match.Status.Should().Be(MatchStatus.OnSale);
        match.AvailableSeatCount.Should().Be(1);
    }

    [Fact]
    public void Should_AnnounceTheVoid_When_ASaleIsTakenBack()
    {
        // Arrange
        var match = SoldMatch(out var seatNumber);
        match.ClearDomainEvents();

        // Act
        match.VoidSeatSale(seatNumber, TestData.Now);

        // Assert - a release and a void are different things and say so, because what happens next differs.
        match.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SeatSaleVoidedDomainEvent>()
            .Which.SeatNumber.Should().Be(seatNumber);
    }

    [Fact]
    public void Should_Refuse_When_TheSeatWasNeverSold()
    {
        // Arrange - held, not sold. Voiding this would credit the counters for a sale that never happened.
        var match = TestData.FootballMatch();
        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        // Act
        var voiding = () => match.VoidSeatSale("MARATON-1-1", TestData.Now);

        // Assert
        voiding.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Seat.NotSold");
    }

    [Fact]
    public void Should_Refuse_When_TheSameSaleIsVoidedTwice()
    {
        // Arrange - a provider redelivering an event is ordinary, so the second attempt has to be refused
        // rather than quietly crediting the counters again.
        var match = SoldMatch(out var seatNumber);
        match.VoidSeatSale(seatNumber, TestData.Now);
        var available = match.AvailableSeatCount;

        // Act
        var voidingAgain = () => match.VoidSeatSale(seatNumber, TestData.Now);

        // Assert
        voidingAgain.Should().Throw<DomainRuleViolationException>();
        match.AvailableSeatCount.Should().Be(available);
    }

    [Fact]
    public void Should_StillVoid_When_TheMatchIsNoLongerOnSale()
    {
        // Arrange - a chargeback arrives whenever the bank feels like it, including after a postponement.
        // Refusing here would leave the money returned and the seat still marked sold.
        var match = SoldMatch(out var seatNumber);
        match.Postpone(TestData.Now.AddDays(30), TestData.Now);

        // Act
        var seat = match.VoidSeatSale(seatNumber, TestData.Now);

        // Assert
        seat.Status.Should().Be(SeatStatus.Available);
        match.Status.Should().Be(MatchStatus.Postponed);
    }

    private static Match SoldMatch(out string seatNumber)
    {
        var match = TestData.FootballMatch();
        seatNumber = "MARATON-1-1";

        match.ReserveSeat(seatNumber, TestData.Holder, TestData.Now);
        match.ConfirmSeatSale(seatNumber, TestData.Holder, TestData.Now);

        return match;
    }
}
