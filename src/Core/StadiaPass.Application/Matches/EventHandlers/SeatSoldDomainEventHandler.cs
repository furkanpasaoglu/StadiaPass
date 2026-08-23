using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Domain.Matches.Events;

namespace StadiaPass.Application.Matches.EventHandlers;

internal sealed partial class SeatSoldDomainEventHandler(
    ICacheService cacheService,
    ILogger<SeatSoldDomainEventHandler> logger) : INotificationHandler<SeatSoldDomainEvent>
{
    public async Task Handle(SeatSoldDomainEvent notification, CancellationToken cancellationToken)
    {
        await cacheService.RemoveAsync(MatchCacheKeys.Upcoming, cancellationToken);

        SeatSold(logger, notification.SeatNumber, notification.MatchId, notification.Price);
    }

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Seat {SeatNumber} of match {MatchId} sold for {Price}")]
    private static partial void SeatSold(ILogger logger, string seatNumber, Guid matchId, decimal price);
}
