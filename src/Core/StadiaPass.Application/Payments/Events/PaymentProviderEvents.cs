namespace StadiaPass.Application.Payments.Events;

/// <summary>
/// A payment the provider says went through.
/// </summary>
/// <remarks>
/// In the ordinary run of things this arrives after the checkout has already issued the ticket and sent the
/// mail, and there is nothing to do about it. It earns its place in the two cases where the checkout did not:
/// a response we never saw - the card was charged and the connection dropped before Stripe could tell us - or
/// a payment that completed later, out of band. In both, this is the only thing that knows money moved.
/// </remarks>
public sealed record PaymentSucceeded(
    string PaymentIntentId,
    Guid? MatchId,
    string? SeatNumber,
    string? HolderReference,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredOnUtc);

/// <summary>
/// The cardholder went to their bank instead of to us.
/// </summary>
/// <remarks>
/// A dispute is a claim, not yet a loss: the funds are held, evidence can be submitted, and it can be won.
/// StadiaPass voids the ticket anyway, because a seat is a physical thing on a specific evening and letting
/// somebody sit in one they have charged back is the worse mistake. Winning a dispute does not put the ticket
/// back on its own - that would need somebody to decide, and there is nobody here to decide it.
/// </remarks>
public sealed record PaymentDisputed(
    string PaymentIntentId,
    string DisputeId,
    string? Reason,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredOnUtc);

/// <summary>
/// Money went back, and not necessarily because this application sent it.
/// </summary>
/// <remarks>
/// Two very different things arrive here wearing the same clothes. One is somebody pressing refund in the
/// Stripe dashboard, where the ticket is still live and has to be voided. The other is this application's own
/// compensation - the refund it issues when a sale loses the race for a seat - echoing back at us, where
/// there is no ticket at all because the sale was rolled back. The consumer cannot tell them apart and does
/// not need to: both are answered by looking for the ticket and doing nothing if there is not one.
/// </remarks>
public sealed record PaymentRefunded(
    string PaymentIntentId,
    decimal AmountRefunded,
    string Currency,
    DateTimeOffset OccurredOnUtc);
