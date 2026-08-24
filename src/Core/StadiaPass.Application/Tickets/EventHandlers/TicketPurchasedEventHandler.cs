using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Tickets.Events;

namespace StadiaPass.Application.Tickets.EventHandlers;

/// <summary>
/// Where ticket rendering and the confirmation mail will live. For now it only says that it was reached,
/// which is enough to prove the message arrives with everything a consumer needs.
/// </summary>
/// <remarks>
/// MediatR awaits its handlers, so today this still runs inside the caller's request - fine while it is one
/// log line, and precisely what the move to a broker fixes. When this handler starts doing real work - a PDF,
/// an SMTP round trip - it wants to be a consumer on a queue, so that a mail server having a bad afternoon
/// cannot slow down a checkout or, worse, fail one that has already taken the money.
/// </remarks>
internal sealed partial class TicketPurchasedEventHandler(ILogger<TicketPurchasedEventHandler> logger)
    : INotificationHandler<TicketPurchasedEvent>
{
    public Task Handle(TicketPurchasedEvent notification, CancellationToken cancellationToken)
    {
        PurchaseCompleted(
            logger,
            notification.TicketId,
            notification.SeatNumber,
            notification.HomeTeam,
            notification.AwayTeam,
            notification.HolderReference);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 3200,
        Level = LogLevel.Information,
        Message = "Ticket generation and mail delivery triggered for ticket {TicketId}: seat {SeatNumber} "
            + "at {HomeTeam} vs {AwayTeam} for holder {HolderReference}")]
    private static partial void PurchaseCompleted(
        ILogger logger,
        Guid ticketId,
        string seatNumber,
        string homeTeam,
        string awayTeam,
        string holderReference);
}
