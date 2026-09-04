using FluentAssertions;
using NSubstitute;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Matches.Queries.GetMatchRevenue;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.UnitTests.Matches;

/// <summary>
/// The one rule this query exists to keep: a refunded ticket is not revenue. It is a single line of code and
/// exactly the kind of line that goes missing, because the number it produces looks perfectly reasonable
/// when it is wrong - nobody can tell 900 lira from 600 by looking at it.
/// </summary>
public sealed class GetMatchRevenueQueryHandlerTests
{
    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly ITicketRepository _ticketRepository = Substitute.For<ITicketRepository>();

    private readonly GetMatchRevenueQueryHandler _handler;

    public GetMatchRevenueQueryHandlerTests() =>
        _handler = new GetMatchRevenueQueryHandler(_matchRepository, _ticketRepository);

    [Fact]
    public async Task Should_CountOnlyLiveTicketsAsRevenue_When_SomeWereRefunded()
    {
        // Arrange - three sold at 100, one of them given back.
        var match = MatchWithRevenueLines(
            new TicketRevenueLine(TicketStatus.Issued, "TRY", Count: 3, Amount: 300m),
            new TicketRevenueLine(TicketStatus.Cancelled, "TRY", Count: 1, Amount: 100m));

        // Act
        var result = await _handler.Handle(new GetMatchRevenueQuery(match.Id), CancellationToken.None);

        // Assert
        result.NetRevenue.Should().Be(300m);
        result.TicketsSold.Should().Be(3);
    }

    [Fact]
    public async Task Should_ReportWhatWasRefunded_When_TicketsWereGivenBack()
    {
        // Arrange - the refunds are reported, not silently netted off: "sold 300, refunded 100" and
        // "sold 200" are the same figure and not the same afternoon.
        var match = MatchWithRevenueLines(
            new TicketRevenueLine(TicketStatus.Issued, "TRY", Count: 3, Amount: 300m),
            new TicketRevenueLine(TicketStatus.Cancelled, "TRY", Count: 1, Amount: 100m));

        // Act
        var result = await _handler.Handle(new GetMatchRevenueQuery(match.Id), CancellationToken.None);

        // Assert
        result.TicketsRefunded.Should().Be(1);
        result.RefundedAmount.Should().Be(100m);
    }

    [Fact]
    public async Task Should_ReportZero_When_NothingHasSoldYet()
    {
        // Arrange - a fixture on sale with no tickets at all answers, rather than failing.
        var match = MatchWithRevenueLines();

        // Act
        var result = await _handler.Handle(new GetMatchRevenueQuery(match.Id), CancellationToken.None);

        // Assert
        result.NetRevenue.Should().Be(0m);
        result.TicketsSold.Should().Be(0);
        result.RefundedAmount.Should().Be(0m);
        result.Currency.Should().Be("TRY");
    }

    [Fact]
    public async Task Should_ReportOccupancyFromTheFixtureCounters_When_SeatsAreSold()
    {
        // Arrange - the six-seat stadium with two seats sold; a held seat is not a sold one.
        var match = MatchWithRevenueLines(
            new TicketRevenueLine(TicketStatus.Issued, "TRY", Count: 2, Amount: 200m));
        SellSeats(match, "MARATON-1-1", "MARATON-1-2");
        HoldSeat(match, "MARATON-1-3");

        // Act
        var result = await _handler.Handle(new GetMatchRevenueQuery(match.Id), CancellationToken.None);

        // Assert
        result.Capacity.Should().Be(6);
        result.SeatsSold.Should().Be(2);
        result.OccupancyPercent.Should().Be(33.33m);
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheMatchDoesNotExist()
    {
        // Arrange
        var unknownMatch = Guid.NewGuid();
        _matchRepository.GetByIdAsync(unknownMatch, Arg.Any<CancellationToken>()).Returns((Match?)null);

        // Act
        var reading = () => _handler.Handle(new GetMatchRevenueQuery(unknownMatch), CancellationToken.None);

        // Assert
        await reading.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_NotAddCurrenciesTogether_When_TheTicketsWereNotAllPricedTheSame()
    {
        // Arrange - impossible today and cheap to guard: 300 TRY and 100 EUR do not make 400 of anything,
        // and a total that says they do is worse than no total.
        var match = MatchWithRevenueLines(
            new TicketRevenueLine(TicketStatus.Issued, "TRY", Count: 3, Amount: 300m),
            new TicketRevenueLine(TicketStatus.Issued, "EUR", Count: 1, Amount: 100m));

        // Act
        var reading = () => _handler.Handle(new GetMatchRevenueQuery(match.Id), CancellationToken.None);

        // Assert
        await reading.Should().ThrowAsync<DomainRuleViolationException>();
    }

    private Match MatchWithRevenueLines(params TicketRevenueLine[] lines)
    {
        var match = TestData.FootballMatch();

        _matchRepository.GetByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _ticketRepository.GetRevenueLinesForMatchAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns(lines);

        return match;
    }

    private static void SellSeats(Match match, params string[] seatNumbers)
    {
        foreach (var seatNumber in seatNumbers)
        {
            match.ReserveSeat(seatNumber, TestData.CurrentUserId, TestData.Now);
            match.ConfirmSeatSale(seatNumber, TestData.CurrentUserId, TestData.Now);
        }
    }

    private static void HoldSeat(Match match, string seatNumber) =>
        match.ReserveSeat(seatNumber, TestData.OtherUserId, TestData.Now);
}
