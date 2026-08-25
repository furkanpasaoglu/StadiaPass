using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Payments.Commands.VoidPaidTicket;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.UnitTests.Payments;

/// <summary>
/// A chargeback has to give the seat back as well as the money. These pin down that the seat, the ticket and
/// the counters move together, and that a transient database failure does not turn into a chargeback this
/// application never applied.
/// </summary>
public sealed class VoidPaidTicketCommandHandlerTests
{
    private const string PaymentIntentId = "pi_3RxKvQ2eZvKYlo2C0abcdefg";

    private readonly ITicketRepository _ticketRepository = Substitute.For<ITicketRepository>();

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    /// <summary>The counter update the repository hands back, so the test can see when it is run.</summary>
    private readonly Func<CancellationToken, Task> _writeCounters =
        Substitute.For<Func<CancellationToken, Task>>();

    private readonly VoidPaidTicketCommandHandler _handler;

    public VoidPaidTicketCommandHandlerTests()
    {
        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        // A substituted transaction that never runs its body would let every assertion below pass against a
        // handler that saves nothing at all, so the fake actually executes what it is given.
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task>>()(call.Arg<CancellationToken>()));

        _writeCounters(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _matchRepository
            .PrepareSeatVoidCounters(Arg.Any<Match>(), Arg.Any<int>())
            .Returns(_writeCounters);

        _handler = new VoidPaidTicketCommandHandler(
            _ticketRepository,
            _matchRepository,
            _unitOfWork,
            _dateTimeProvider,
            NullLogger<VoidPaidTicketCommandHandler>.Instance);
    }

    [Fact]
    public async Task Should_PutTheSeatBackOnSale_When_ThePaymentIsTakenBack()
    {
        // Arrange
        var (match, ticket) = GivenASoldSeat();

        // Act
        await _handler.Handle(new VoidPaidTicketCommand(PaymentIntentId, "chargeback"), CancellationToken.None);

        // Assert - a seat left marked Sold after the money has gone is a seat nobody paid for and nobody can
        // buy.
        TestData.SeatOf(match, TestData.SeatNumber).Status.Should().Be(SeatStatus.Available);
        ticket.Status.Should().Be(TicketStatus.Cancelled);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_LeaveTheMatchCountersToTheDatabase_When_TheSaleIsVoided()
    {
        // Arrange
        var (match, _) = GivenASoldSeat();

        // Act
        await _handler.Handle(new VoidPaidTicketCommand(PaymentIntentId, "chargeback"), CancellationToken.None);

        // Assert - a relative update, and the match row taken last: it is the coarsest lock in the system,
        // so it is held from that statement to the commit rather than across the whole transaction.
        _matchRepository.Received(1).PrepareSeatVoidCounters(match, 1);
        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _writeCounters(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Should_VoidTheSaleOnlyOnce_When_TheTransactionIsRetried()
    {
        // Arrange - the retrying execution strategy runs the delegate again after a transient failure, and
        // the rollback undoes the database and nothing else. A second pass over the transitions would find a
        // seat that is no longer Sold and a ticket that is no longer live, and both would throw.
        var (match, ticket) = GivenASoldSeat();

        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var operation = call.Arg<Func<CancellationToken, Task>>();

                await operation(call.Arg<CancellationToken>());
                await operation(call.Arg<CancellationToken>());
            });

        // Act
        var voiding = async () =>
            await _handler.Handle(new VoidPaidTicketCommand(PaymentIntentId, "chargeback"), CancellationToken.None);

        // Assert - a blink of the network must not cost a chargeback.
        await voiding.Should().NotThrowAsync();
        TestData.SeatOf(match, TestData.SeatNumber).Status.Should().Be(SeatStatus.Available);
        ticket.Status.Should().Be(TicketStatus.Cancelled);
    }

    [Fact]
    public async Task Should_DoNothing_When_ThePaymentHasNoLiveTicket()
    {
        // Arrange - the ordinary case for a refund this application issued itself: the sale it was
        // compensating never committed.
        _ticketRepository
            .GetByPaymentIntentAsync(PaymentIntentId, Arg.Any<CancellationToken>())
            .Returns((Ticket?)null);

        // Act
        await _handler.Handle(new VoidPaidTicketCommand(PaymentIntentId, "refund"), CancellationToken.None);

        // Assert
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _matchRepository.DidNotReceive().PrepareSeatVoidCounters(Arg.Any<Match>(), Arg.Any<int>());
    }

    private (Match Match, Ticket Ticket) GivenASoldSeat()
    {
        var match = TestData.FootballMatch();
        match.ReserveSeat(TestData.SeatNumber, TestData.CurrentUserId, TestData.Now);
        var seat = match.ConfirmSeatSale(TestData.SeatNumber, TestData.CurrentUserId, TestData.Now);
        var ticket = Ticket.IssueFor(match, seat, PaymentIntentId, TestData.Now);

        _ticketRepository
            .GetByPaymentIntentAsync(PaymentIntentId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        _matchRepository
            .GetWithSeatAsync(match.Id, TestData.SeatNumber, Arg.Any<CancellationToken>())
            .Returns(match);

        return (match, ticket);
    }
}
