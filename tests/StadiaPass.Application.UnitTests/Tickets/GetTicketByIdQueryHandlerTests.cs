using NSubstitute;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Tickets.Queries.GetTicketById;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Tickets;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.UnitTests.Tickets;

/// <summary>
/// Everyone who reaches this query holds Tickets.View - a customer needs it to open their own ticket. That
/// makes the endpoint policy alone insufficient: without an ownership check any signed-in customer could
/// read a stranger's ticket, seat, price and buyer included, by guessing an id.
/// </summary>
public sealed class GetTicketByIdQueryHandlerTests
{
    private readonly ITicketRepository _ticketRepository = Substitute.For<ITicketRepository>();

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private readonly GetTicketByIdQueryHandler _handler;

    public GetTicketByIdQueryHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);

        _handler = new GetTicketByIdQueryHandler(_ticketRepository, _currentUser);
    }

    [Fact]
    public async Task Should_ReturnTheTicket_When_TheCallerIsTheHolder()
    {
        // Arrange
        var ticket = TicketIssuedTo(TestData.CurrentUserId);
        _currentUser.Reference.Returns(TestData.CurrentUserId);

        // Act
        var result = await _handler.Handle(new GetTicketByIdQuery(ticket.Id), CancellationToken.None);

        // Assert
        result.Id.Should().Be(ticket.Id);
        result.AccessCode.Should().Be(ticket.AccessCode);
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheTicketBelongsToSomebodyElse()
    {
        // Arrange
        var ticket = TicketIssuedTo(TestData.OtherUserId);
        _currentUser.Reference.Returns(TestData.CurrentUserId);
        _currentUser.HasPermission(StadiaPassPermissions.Tickets.ViewAll).Returns(false);

        // Act
        var reading = () => _handler.Handle(new GetTicketByIdQuery(ticket.Id), CancellationToken.None);

        // Assert - not "forbidden": that would confirm to a stranger that the guessed id is a real ticket.
        await reading.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_ReturnTheTicket_When_TheCallerHoldsViewAll()
    {
        // Arrange - the box office has to be able to look up the ticket in front of it.
        var ticket = TicketIssuedTo(TestData.OtherUserId);
        _currentUser.Reference.Returns(TestData.CurrentUserId);
        _currentUser.HasPermission(StadiaPassPermissions.Tickets.ViewAll).Returns(true);

        // Act
        var result = await _handler.Handle(new GetTicketByIdQuery(ticket.Id), CancellationToken.None);

        // Assert
        result.HolderReference.Should().Be(TestData.OtherUserId);
    }

    private Ticket TicketIssuedTo(string holderReference)
    {
        var match = TestData.FootballMatch();

        match.ReserveSeat(TestData.SeatNumber, holderReference, TestData.Now);

        var seat = match.ConfirmSeatSale(TestData.SeatNumber, holderReference, TestData.Now);
        var ticket = Ticket.IssueFor(match, seat, "pi_test", TestData.Now);

        _ticketRepository.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);

        return ticket;
    }
}
