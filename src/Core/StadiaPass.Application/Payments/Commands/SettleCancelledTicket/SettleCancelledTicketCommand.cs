using MediatR;

namespace StadiaPass.Application.Payments.Commands.SettleCancelledTicket;

/// <summary>
/// Settles one ticket of a fixture that has been called off: the seat comes back, the ticket is cancelled,
/// and the money owed is written down.
/// </summary>
/// <remarks>
/// One ticket at a time, and not a loop inside a single transaction, because each of these owes somebody
/// money. A refund the provider refuses has to be able to fail on its own and be tried again on its own,
/// rather than rolling back several hundred settlements that were perfectly good.
/// <para>
/// Addressed by the payment rather than by the ticket, which is what makes running it twice harmless: the
/// lookup only ever returns a ticket that is still live, so the second pass finds nothing and writes nothing.
/// </para>
/// </remarks>
public sealed record SettleCancelledTicketCommand(string PaymentIntentId, string Reason) : IRequest;
