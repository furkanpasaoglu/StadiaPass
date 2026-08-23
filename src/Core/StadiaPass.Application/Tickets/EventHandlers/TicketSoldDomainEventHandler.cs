using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Domain.Tickets.Events;

namespace StadiaPass.Application.Tickets.EventHandlers;

internal sealed partial class TicketSoldDomainEventHandler(
    ICacheService cacheService,
    ILogger<TicketSoldDomainEventHandler> logger) : INotificationHandler<TicketSoldDomainEvent>
{
    public async Task Handle(TicketSoldDomainEvent notification, CancellationToken cancellationToken)
    {
        await cacheService.RemoveAsync("matches:upcoming", cancellationToken);

        TicketSold(logger, notification.TicketId, notification.MatchId, notification.Price);
    }

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Ticket {TicketId} for match {MatchId} sold for {Price}")]
    private static partial void TicketSold(ILogger logger, Guid ticketId, Guid matchId, decimal price);
}
