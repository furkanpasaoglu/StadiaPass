using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Payments.Events;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Application.Payments.Commands.SettleCancelledTicket;

internal sealed partial class SettleCancelledTicketCommandHandler(
    ITicketRepository ticketRepository,
    IMatchRepository matchRepository,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILogger<SettleCancelledTicketCommandHandler> logger) : IRequestHandler<SettleCancelledTicketCommand>
{
    public async Task Handle(SettleCancelledTicketCommand request, CancellationToken cancellationToken)
    {
        var ticket = await ticketRepository.GetByPaymentIntentAsync(request.PaymentIntentId, cancellationToken);

        if (ticket is null)
        {
            // Already settled, or refunded from the provider's dashboard before this arrived. The lookup only
            // returns live tickets, so this is the line that makes settling a fixture safe to run again.
            AlreadySettled(logger, request.PaymentIntentId);

            return;
        }

        var match = await matchRepository.GetWithSeatAsync(
            ticket.MatchId, ticket.SeatNumber.ToString(), cancellationToken);

        if (match is null)
        {
            // A ticket without its match is not something this can put right on its own.
            MatchMissing(logger, ticket.Id, ticket.MatchId);

            return;
        }

        var seatNumber = ticket.SeatNumber.ToString();
        var now = dateTimeProvider.UtcNow;

        // Memory only, and outside the transaction on purpose. The retrying execution strategy runs the
        // delegate below again after a transient failure, and neither of these two lines survives being run
        // twice: the seat is no longer Sold and the ticket is no longer live, so both would throw.
        match.VoidSeatSale(seatNumber, now);
        ticket.Cancel(now);

        // The debt is staged here and written by the save inside the transaction, so it lands with the
        // cancellation or not at all. The other order is the one that loses money: cancel the ticket, stop,
        // and the customer has no seat, no ticket and nothing that owes them anything.
        //
        // The amount and the payment it is refunded against are read from the same ticket, so they cannot
        // name different sales. The ticket is the record of what was charged; the seat only knows what it
        // costs.
        outbox.Enqueue(new RefundOwedEvent(
            ticket.PaymentIntentId,
            ticket.Price.Amount,
            ticket.Price.Currency,
            match.Id,
            seatNumber,
            request.Reason,
            now));

        // Counters out of the save's hands, written last, as everywhere else: the match row is held from that
        // statement to the commit rather than across the whole transaction. It finds a row that already says
        // Cancelled, so its sold-out test fails and the status is left where the cancellation put it.
        var writeCounters = matchRepository.PrepareSeatVoidCounters(match, 1);

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    await unitOfWork.SaveChangesAsync(token);

                    await writeCounters(token);
                },
                cancellationToken);

            Settled(logger, ticket.Id, seatNumber, ticket.Price.Amount);
        }
        catch (ConcurrencyConflictException exception)
        {
            // Thrown on rather than swallowed. The caller is a message consumer, so the broker redelivers and
            // the next attempt sees the seat as it now is; swallowing would leave a ticket nobody refunds.
            SettlementLostTheRace(logger, ticket.Id, seatNumber, exception);

            throw;
        }
    }

    [LoggerMessage(
        EventId = 3400,
        Level = LogLevel.Information,
        Message = "Ticket {TicketId} for seat {SeatNumber} was settled after a cancellation; {Amount} is "
            + "owed back")]
    private static partial void Settled(ILogger logger, Guid ticketId, string seatNumber, decimal amount);

    [LoggerMessage(
        EventId = 3401,
        Level = LogLevel.Information,
        Message = "Payment {PaymentIntentId} has no live ticket, so its cancellation was settled already")]
    private static partial void AlreadySettled(ILogger logger, string paymentIntentId);

    [LoggerMessage(
        EventId = 3402,
        Level = LogLevel.Error,
        Message = "Ticket {TicketId} names match {MatchId}, which could not be loaded; the seat could not be "
            + "put back and the refund needs a person")]
    private static partial void MatchMissing(ILogger logger, Guid ticketId, Guid matchId);

    [LoggerMessage(
        EventId = 3403,
        Level = LogLevel.Warning,
        Message = "Settling ticket {TicketId} for seat {SeatNumber} lost a race and will be tried again")]
    private static partial void SettlementLostTheRace(
        ILogger logger,
        Guid ticketId,
        string seatNumber,
        Exception exception);
}
