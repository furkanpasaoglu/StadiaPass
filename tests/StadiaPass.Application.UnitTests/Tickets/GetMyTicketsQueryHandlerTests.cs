using NSubstitute;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Tickets.Queries.GetMyTickets;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.UnitTests.Tickets;

/// <summary>
/// The stubs somebody sees on their own account. A stub carrying a seat number and nothing else cannot be
/// told from the next one when they hold tickets to more than one fixture, so the match is joined on - and
/// with it the fixture's own status, which is what lets the page say a match was called off rather than
/// showing a bare cancelled ticket and leaving the holder to guess.
/// </summary>
public sealed class GetMyTicketsQueryHandlerTests
{
    private readonly ITicketRepository _ticketRepository = Substitute.For<ITicketRepository>();

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private readonly GetMyTicketsQueryHandler _handler;

    public GetMyTicketsQueryHandlerTests()
    {
        _currentUser.Reference.Returns(TestData.CurrentUserId);

        _handler = new GetMyTicketsQueryHandler(_ticketRepository, _matchRepository, _currentUser);
    }

    [Fact]
    public async Task Should_NameTheFixtureEachTicketIsFor()
    {
        // Arrange
        var (match, ticket) = GivenATicket();

        // Act
        var tickets = await _handler.Handle(new GetMyTicketsQuery(), CancellationToken.None);

        // Assert
        var stub = tickets.Should().ContainSingle().Subject;
        stub.HomeTeam.Should().Be("Fenerbahce");
        stub.AwayTeam.Should().Be("Galatasaray");
        stub.VenueName.Should().Be(match.VenueName);
        stub.KickOffUtc.Should().Be(match.KickOffUtc);
        stub.SeatNumber.Should().Be(TestData.SeatNumber);
        stub.AccessCode.Should().Be(ticket.AccessCode);
    }

    [Fact]
    public async Task Should_TellTheHolderTheFixtureWasCalledOff()
    {
        // Arrange
        var (match, _) = GivenATicket();
        match.Cancel(TestData.Now);

        // Act
        var tickets = await _handler.Handle(new GetMyTicketsQuery(), CancellationToken.None);

        // Assert - without the fixture's status the page can only show a cancelled ticket, which reads as
        // something the holder did rather than something that was done to their match.
        tickets.Should().ContainSingle().Which.MatchStatus.Should().Be("Cancelled");
    }

    [Fact]
    public async Task Should_ReadTheFixturesInOneGo()
    {
        // Arrange - two seats at the same fixture, which is what a pair of friends looks like.
        var match = TestData.FootballMatch();
        var first = ATicketAt(match, "MARATON-1-1");
        var second = ATicketAt(match, "MARATON-1-2");

        _ticketRepository
            .GetByHolderAsync(TestData.CurrentUserId, Arg.Any<CancellationToken>())
            .Returns([first, second]);
        _matchRepository
            .GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([match]);

        // Act
        await _handler.Handle(new GetMyTicketsQuery(), CancellationToken.None);

        // Assert - a round trip per stub would cost a season ticket holder one query per match they have ever
        // been to, and most of those name the same handful of fixtures.
        await _matchRepository.Received(1).GetByIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_StillShowTheStub_When_ItsFixtureIsGone()
    {
        // Arrange
        GivenATicket();
        _matchRepository
            .GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        // Act
        var tickets = await _handler.Handle(new GetMyTicketsQuery(), CancellationToken.None);

        // Assert - a ticket outliving its fixture is not something to show its holder an error for. The seat,
        // the price and the code are theirs either way.
        var stub = tickets.Should().ContainSingle().Subject;
        stub.SeatNumber.Should().Be(TestData.SeatNumber);
        stub.Price.Should().Be(100m);
        stub.HomeTeam.Should().BeEmpty();
        stub.MatchStatus.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_AskForNoFixtures_When_TheAccountHasNoTickets()
    {
        // Arrange
        _ticketRepository
            .GetByHolderAsync(TestData.CurrentUserId, Arg.Any<CancellationToken>())
            .Returns([]);

        // Act
        var tickets = await _handler.Handle(new GetMyTicketsQuery(), CancellationToken.None);

        // Assert - there is no sensible query for an empty list of identifiers, and no reason to ask one.
        tickets.Should().BeEmpty();
        await _matchRepository.DidNotReceive().GetByIdsAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    private (Match Match, Ticket Ticket) GivenATicket()
    {
        var match = TestData.FootballMatch();
        var ticket = ATicketAt(match, TestData.SeatNumber);

        _ticketRepository
            .GetByHolderAsync(TestData.CurrentUserId, Arg.Any<CancellationToken>())
            .Returns([ticket]);

        _matchRepository
            .GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([match]);

        return (match, ticket);
    }

    private static Ticket ATicketAt(Match match, string seatNumber)
    {
        match.ReserveSeat(seatNumber, TestData.CurrentUserId, TestData.Now);
        var seat = match.ConfirmSeatSale(seatNumber, TestData.CurrentUserId, TestData.Now);

        return Ticket.IssueFor(match, seat, $"pi_{seatNumber}", TestData.Now);
    }
}
