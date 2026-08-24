using MediatR;

namespace StadiaPass.Application.Tickets.Events;

/// <summary>
/// Announced once a purchase is completely done: the card was charged, the seat is sold, and the ticket row
/// is committed. Everything that comes after - rendering the ticket, sending the confirmation mail - hangs
/// off this, so none of it can hold up the answer the customer is waiting for.
/// </summary>
/// <remarks>
/// Shaped as a message rather than as a domain event on purpose. It carries everything a consumer needs and
/// reaches back for nothing, so putting it on RabbitMQ later means changing who publishes it, not what it
/// says: <c>publisher.Publish</c> becomes <c>bus.Publish</c> and the consumers keep their signatures.
/// <see cref="AccessCode"/> is named the way it is so the log destructuring policy masks it - the code is
/// what gets somebody through the turnstile, and a log file is no place for it.
/// </remarks>
public sealed record TicketPurchasedEvent(
    Guid TicketId,
    string AccessCode,
    Guid MatchId,
    string HomeTeam,
    string AwayTeam,
    string VenueName,
    DateTimeOffset KickOffUtc,
    string SeatNumber,
    decimal Price,
    string Currency,
    string HolderReference,
    string PaymentTransactionId,
    DateTimeOffset PurchasedAtUtc) : INotification;
