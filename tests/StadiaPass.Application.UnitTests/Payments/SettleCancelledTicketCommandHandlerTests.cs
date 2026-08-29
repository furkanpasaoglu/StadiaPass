using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Payments.Commands.SettleCancelledTicket;
using StadiaPass.Application.Payments.Events;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.UnitTests.Payments;

/// <summary>
/// One ticket of a cancelled fixture: the seat comes back, the ticket is cancelled, and the money owed is
/// written down - all by the same save, so the three cannot come apart.
/// </summary>
/// <remarks>
/// The order that matters is the one that cannot lose money. If the ticket were cancelled first and the debt
/// recorded second, a process that stopped in between would leave a customer with no ticket, no seat and no
/// refund owed to them by anything. Written together, the worst case is a refund recorded for a ticket that
/// is settled anyway - and the provider treats a repeated refund of the same charge as the same refund.
/// </remarks>
public sealed class SettleCancelledTicketCommandHandlerTests
{
    private const string PaymentIntentId = "pi_3RxKvQ2eZvKYlo2C0abcdefg";

    private const string Reason = "the match was cancelled: the pitch is frozen";

    private readonly ITicketRepository _ticketRepository = Substitute.For<ITicketRepository>();

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    private readonly Func<CancellationToken, Task> _writeCounters =
        Substitute.For<Func<CancellationToken, Task>>();

    private readonly SettleCancelledTicketCommandHandler _handler;

    public SettleCancelledTicketCommandHandlerTests()
    {
        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task>>()(call.Arg<CancellationToken>()));

        _writeCounters(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _matchRepository
            .PrepareSeatVoidCounters(Arg.Any<Match>(), Arg.Any<int>())
            .Returns(_writeCounters);

        _handler = new SettleCancelledTicketCommandHandler(
            _ticketRepository,
            _matchRepository,
            _outbox,
            _unitOfWork,
            _dateTimeProvider,
            NullLogger<SettleCancelledTicketCommandHandler>.Instance);
    }

    [Fact]
    public async Task Should_DoNothing_When_TheTicketHasAlreadyBeenSettled()
    {
        // Arrange - the lookup only ever returns a live ticket, so a redelivered message finds nothing. This
        // is what makes settling a fixture safe to run again after a half-finished pass.
        _ticketRepository
            .GetByPaymentIntentAsync(PaymentIntentId, Arg.Any<CancellationToken>())
            .Returns((Ticket?)null);

        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - a second refund for a ticket already paid back is money out of the door twice.
        _outbox.DidNotReceive().Enqueue(Arg.Any<object>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_TakeTheSeatBackAndCancelTheTicket()
    {
        // Arrange
        var (match, ticket) = GivenASoldSeat();

        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert
        TestData.SeatOf(match, TestData.SeatNumber).Status.Should().Be(SeatStatus.Available);
        ticket.Status.Should().Be(TicketStatus.Cancelled);
    }

    [Fact]
    public async Task Should_OweBackWhatTheCustomerActuallyPaid()
    {
        // Arrange
        var (match, _) = GivenASoldSeat();
        RefundOwedEvent? owed = null;
        _outbox.Enqueue(Arg.Do<object>(message =>
        {
            if (message is RefundOwedEvent refund)
            {
                owed = refund;
            }
        }));

        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - the amount and the charge it is refunded against are read from the same ticket, so they
        // cannot end up naming different sales. A hundred is the MARATON seat price of this fixture: base
        // price 100 at a block with no multiplier.
        owed.Should().NotBeNull();
        owed!.Amount.Should().Be(100m);
        owed.PaymentTransactionId.Should().Be(PaymentIntentId);
        owed.Reason.Should().Be(Reason);
        owed.MatchId.Should().Be(match.Id);
        owed.SeatNumber.Should().Be(TestData.SeatNumber);
    }

    [Fact]
    public async Task Should_WriteTheDebtAndTheCancellationTogether()
    {
        // Arrange
        GivenASoldSeat();

        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - staged before the transaction and written by the save inside it. Recording the debt after
        // the commit leaves a window where the customer has lost their ticket and nothing owes them anything.
        Received.InOrder(() =>
        {
            _outbox.Enqueue(Arg.Any<RefundOwedEvent>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Should_TakeTheMatchRowLast()
    {
        // Arrange
        var (match, _) = GivenASoldSeat();

        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert
        _matchRepository.Received(1).PrepareSeatVoidCounters(match, 1);
        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _writeCounters(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Should_LetTheFailureOut_When_SomebodyWroteTheSeatFirst()
    {
        // Arrange
        GivenASoldSeat();
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("the seat moved"));

        // Act
        var settling = async () => await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - swallowing here would leave a ticket nobody refunds. The caller is a message consumer, so
        // throwing hands it back to the broker and the next attempt sees the seat as it now is.
        await settling.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    private (Match Match, Ticket Ticket) GivenASoldSeat()
    {
        var match = TestData.FootballMatch();
        match.ReserveSeat(TestData.SeatNumber, TestData.CurrentUserId, TestData.Now);
        var seat = match.ConfirmSeatSale(TestData.SeatNumber, TestData.CurrentUserId, TestData.Now);
        var ticket = Ticket.IssueFor(match, seat, PaymentIntentId, TestData.Now);

        match.Cancel(TestData.Now);

        _ticketRepository
            .GetByPaymentIntentAsync(PaymentIntentId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        _matchRepository
            .GetWithSeatAsync(match.Id, TestData.SeatNumber, Arg.Any<CancellationToken>())
            .Returns(match);

        return (match, ticket);
    }

    private static SettleCancelledTicketCommand ACommand() => new(PaymentIntentId, Reason);
}
