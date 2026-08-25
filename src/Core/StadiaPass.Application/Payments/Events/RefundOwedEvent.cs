namespace StadiaPass.Application.Payments.Events;

/// <summary>
/// Money this system took and could not give back, written down so that something other than a log line
/// remembers it.
/// </summary>
/// <remarks>
/// The checkout charges the card and then writes the sale, and the write can still fail - it can lose the
/// race for the seat, or the connection can go. The charge is undone by refunding it, and until now a refund
/// that itself failed left nothing behind but an <c>Error</c> log. If nobody was watching that logger, or the
/// line rotated away, the money stayed with the provider and no part of the system knew.
/// <para>
/// So the second attempt is not an attempt at all: it is a row. It goes through the outbox like every other
/// message, which means it is written in a transaction, published by the sweeper, retried by the broker and
/// counted by the dead-message gauge if it never succeeds. The refund is idempotent at the provider - the
/// Stripe adapter keys it on the payment - so being delivered more than once gives the money back once.
/// </para>
/// </remarks>
public sealed record RefundOwedEvent(
    string PaymentTransactionId,
    decimal Amount,
    string Currency,
    Guid MatchId,
    string SeatNumber,
    string Reason,
    DateTimeOffset OccurredOnUtc);
