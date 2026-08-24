using MassTransit;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Tickets.Events;

namespace StadiaPass.Infrastructure.Messaging;

/// <summary>
/// Where ticket rendering and the confirmation mail will live. For now it only reports that the message
/// arrived, which is enough to show the whole path works: sale committed, row written, worker swept, broker
/// routed, consumer woken.
/// </summary>
/// <remarks>
/// The outbox delivers at least once, so this will one day see the same purchase twice - a broker
/// acknowledgement can be lost after the message was handled perfectly well. Whatever ends up in here has to
/// be safe to run again: check for the ticket before rendering it, and for the mail before sending it. Two
/// identical confirmation mails is a small embarrassment; two charges would not be.
/// </remarks>
internal sealed partial class TicketPurchasedEventConsumer(ILogger<TicketPurchasedEventConsumer> logger)
    : IConsumer<TicketPurchasedEvent>
{
    public Task Consume(ConsumeContext<TicketPurchasedEvent> context)
    {
        var purchase = context.Message;

        PurchaseConsumed(
            logger,
            purchase.TicketId,
            purchase.SeatNumber,
            purchase.HomeTeam,
            purchase.AwayTeam,
            purchase.HolderReference);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 6300,
        Level = LogLevel.Information,
        Message = "TicketPurchasedEvent consumed from RabbitMQ for ticket {TicketId}: seat {SeatNumber} at "
            + "{HomeTeam} vs {AwayTeam} for holder {HolderReference} - ticket rendering would start here")]
    private static partial void PurchaseConsumed(
        ILogger logger,
        Guid ticketId,
        string seatNumber,
        string homeTeam,
        string awayTeam,
        string holderReference);
}
