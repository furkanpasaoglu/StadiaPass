using MassTransit;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Tickets.Events;
using StadiaPass.Infrastructure.Email;

namespace StadiaPass.Infrastructure.Messaging;

/// <summary>
/// Turns a completed purchase into the confirmation the customer is waiting for. This is the far side of the
/// outbox: the sale committed, the row was swept up, the broker routed it, and now the slow work happens
/// where it cannot hold up or fail a checkout.
/// </summary>
/// <remarks>
/// The outbox delivers at least once, so this will one day see the same purchase twice - a broker
/// acknowledgement can be lost after the message was handled perfectly well. Two identical confirmation
/// mails is a small embarrassment and the honest cost of that guarantee; anything added here that is not
/// safe to run twice needs its own check first.
/// </remarks>
internal sealed partial class TicketPurchasedEventConsumer(
    IEmailService emailService,
    ILogger<TicketPurchasedEventConsumer> logger) : IConsumer<TicketPurchasedEvent>
{
    public async Task Consume(ConsumeContext<TicketPurchasedEvent> context)
    {
        var purchase = context.Message;

        PurchaseConsumed(
            logger,
            purchase.TicketId,
            purchase.SeatNumber,
            purchase.HomeTeam,
            purchase.AwayTeam,
            purchase.HolderReference);

        if (purchase.HolderEmail is not { Length: > 0 } recipient)
        {
            // The ticket is real and the customer can still see it in the app; only the mail has nowhere to
            // go. Said out loud with the ticket id, because somebody may have to send it by hand.
            NoAddress(logger, purchase.TicketId, purchase.HolderReference);

            return;
        }

        var sent = await emailService.SendEmailAsync(
            recipient,
            TicketConfirmationEmail.SubjectFor(purchase),
            TicketConfirmationEmail.BodyFor(purchase),
            context.CancellationToken);

        if (!sent)
        {
            // Thrown rather than swallowed, so the broker does what a broker is for. MassTransit redelivers
            // the message, and if it never goes through it lands in the error queue where somebody can see
            // it - both of which are switched off by quietly returning here instead.
            throw new InvalidOperationException(
                $"The confirmation for ticket {purchase.TicketId} could not be sent to {recipient}.");
        }
    }

    [LoggerMessage(
        EventId = 6300,
        Level = LogLevel.Information,
        Message = "TicketPurchasedEvent consumed from RabbitMQ for ticket {TicketId}: seat {SeatNumber} at "
            + "{HomeTeam} vs {AwayTeam} for holder {HolderReference}")]
    private static partial void PurchaseConsumed(
        ILogger logger,
        Guid ticketId,
        string seatNumber,
        string homeTeam,
        string awayTeam,
        string holderReference);

    [LoggerMessage(
        EventId = 6301,
        Level = LogLevel.Warning,
        Message = "Ticket {TicketId} was bought by {HolderReference}, who has no email address on their "
            + "account, so no confirmation could be sent")]
    private static partial void NoAddress(ILogger logger, Guid ticketId, string holderReference);
}
