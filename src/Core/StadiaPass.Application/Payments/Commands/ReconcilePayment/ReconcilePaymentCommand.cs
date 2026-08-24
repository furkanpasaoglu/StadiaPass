using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Application.Payments.Commands.ReconcilePayment;

/// <summary>
/// Checks that a payment the provider says succeeded actually produced a ticket.
/// </summary>
/// <remarks>
/// Almost always it did, and this does nothing: the checkout confirms the charge synchronously and has
/// already issued the ticket and sent the mail by the time this arrives. It exists for the case that used to
/// be invisible - the card was charged and the response never came back, so the checkout believed it had
/// failed. Stripe knows about that payment and, until this ran, nothing here did.
/// </remarks>
public sealed record ReconcilePaymentCommand(
    string PaymentIntentId,
    Guid? MatchId,
    string? SeatNumber,
    string? HolderReference,
    decimal Amount,
    string Currency) : IRequest;

internal sealed partial class ReconcilePaymentCommandHandler(
    ITicketRepository ticketRepository,
    ILogger<ReconcilePaymentCommandHandler> logger) : IRequestHandler<ReconcilePaymentCommand>
{
    public async Task Handle(ReconcilePaymentCommand request, CancellationToken cancellationToken)
    {
        var ticket = await ticketRepository.GetByPaymentIntentAsync(request.PaymentIntentId, cancellationToken);

        if (ticket is not null)
        {
            Reconciled(logger, request.PaymentIntentId, ticket.Id);

            return;
        }

        // Deliberately loud. Somebody has been charged for a seat this system does not believe it sold, and
        // the only two honest endings are giving them the seat or giving them the money - both of which are
        // decisions a person makes. The metadata is in the line so they do not have to go looking.
        MoneyWithoutATicket(
            logger,
            request.PaymentIntentId,
            request.Amount,
            request.Currency,
            request.MatchId,
            request.SeatNumber,
            request.HolderReference);
    }

    [LoggerMessage(
        EventId = 3400,
        Level = LogLevel.Debug,
        Message = "Payment {PaymentIntentId} is accounted for by ticket {TicketId}")]
    private static partial void Reconciled(ILogger logger, string paymentIntentId, Guid ticketId);

    [LoggerMessage(
        EventId = 3401,
        Level = LogLevel.Error,
        Message = "The provider says payment {PaymentIntentId} took {Amount} {Currency} but no ticket was "
            + "issued for it. Match {MatchId}, seat {SeatNumber}, holder {HolderReference}. Somebody has paid "
            + "for a seat this system does not think it sold")]
    private static partial void MoneyWithoutATicket(
        ILogger logger,
        string paymentIntentId,
        decimal amount,
        string currency,
        Guid? matchId,
        string? seatNumber,
        string? holderReference);
}
