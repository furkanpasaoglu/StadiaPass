namespace StadiaPass.Application.Tickets.Events;

/// <summary>
/// Announced once a purchase is completely done: the card was charged, the seat is sold, and the ticket row
/// is committed. Everything that comes after - rendering the ticket, sending the confirmation mail - hangs
/// off this, so none of it can hold up the answer the customer is waiting for.
/// </summary>
/// <remarks>
/// A message, not a domain event: it carries everything a consumer needs and reaches back for nothing, which
/// is what lets it cross a broker. It is deliberately not a MediatR notification any more - nothing publishes
/// it in process. It goes to the outbox inside the sale transaction and reaches consumers over RabbitMQ.
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
    DateTimeOffset PurchasedAtUtc);
