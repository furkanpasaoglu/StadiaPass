using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Payments.Commands.VoidPaidTicket;

/// <summary>
/// Cancels the ticket, puts the seat back on offer and corrects the counters, all in one transaction.
/// </summary>
/// <remarks>
/// The seat is the reason this cannot just cancel a row. A ticket is a claim on a specific seat on a specific
/// evening: leaving it sold after the money has gone would keep a seat that nobody has paid for out of the
/// hands of somebody who would.
/// </remarks>
internal sealed partial class VoidPaidTicketCommandHandler(
    ITicketRepository ticketRepository,
    IMatchRepository matchRepository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILogger<VoidPaidTicketCommandHandler> logger) : IRequestHandler<VoidPaidTicketCommand>
{
    public async Task Handle(VoidPaidTicketCommand request, CancellationToken cancellationToken)
    {
        var ticket = await ticketRepository.GetByPaymentIntentAsync(request.PaymentIntentId, cancellationToken);

        if (ticket is null)
        {
            // The ordinary case for a refund this application issued itself: the sale it was compensating
            // never committed, so there is no ticket and nothing to undo.
            NoTicket(logger, request.PaymentIntentId, request.Reason);

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
        // twice: the seat is no longer Sold and the ticket is no longer live, so both would throw. A dropped
        // connection would become a chargeback this application never applied.
        match.VoidSeatSale(seatNumber, now);
        ticket.Cancel(now);

        // Counters out of the save's hands now, written last, as everywhere else: the match row is held from
        // that statement to the commit rather than across the whole transaction.
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

            TicketVoided(logger, ticket.Id, seatNumber, request.Reason);
        }
        catch (ConcurrencyConflictException exception)
        {
            // Somebody wrote that seat between the read and the write. Thrown on rather than swallowed: the
            // caller is a message consumer, so the broker redelivers and the next attempt sees the seat as
            // it now is. Losing a chargeback because of a race would be a real loss.
            VoidLostTheRace(logger, ticket.Id, seatNumber, exception);

            throw;
        }
    }

    [LoggerMessage(
        EventId = 3300,
        Level = LogLevel.Information,
        Message = "Ticket {TicketId} for seat {SeatNumber} was voided ({Reason}); the seat is back on sale")]
    private static partial void TicketVoided(ILogger logger, Guid ticketId, string seatNumber, string reason);

    [LoggerMessage(
        EventId = 3301,
        Level = LogLevel.Information,
        Message = "Payment {PaymentIntentId} ({Reason}) has no live ticket, so there was nothing to void")]
    private static partial void NoTicket(ILogger logger, string paymentIntentId, string reason);

    [LoggerMessage(
        EventId = 3302,
        Level = LogLevel.Error,
        Message = "Ticket {TicketId} names match {MatchId}, which could not be loaded; the seat could not be "
            + "put back and needs a person")]
    private static partial void MatchMissing(ILogger logger, Guid ticketId, Guid matchId);

    [LoggerMessage(
        EventId = 3303,
        Level = LogLevel.Warning,
        Message = "Voiding ticket {TicketId} for seat {SeatNumber} lost a race and will be tried again")]
    private static partial void VoidLostTheRace(
        ILogger logger,
        Guid ticketId,
        string seatNumber,
        Exception exception);
}
