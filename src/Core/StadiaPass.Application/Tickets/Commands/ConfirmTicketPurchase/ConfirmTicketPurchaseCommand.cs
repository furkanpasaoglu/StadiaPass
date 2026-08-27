using MediatR;

namespace StadiaPass.Application.Tickets.Commands.ConfirmTicketPurchase;

/// <summary>
/// Turns the caller's own seat reservation into a sale and issues the ticket, once the card has been charged.
/// The card details are used for that single charge and are never stored; the number and the security code
/// are masked before any log event is written.
/// </summary>
/// <param name="AttemptId">
/// Identifies one go at paying for this seat, and is what the provider's idempotency key is built from. A
/// double-click on the same checkout form repeats the same attempt and is charged once; reaching for another
/// card after a decline is a different attempt and must not be answered with the first one's result. A caller
/// that sends nothing is given an id here, which costs it double-click protection and nothing else.
/// </param>
public sealed record ConfirmTicketPurchaseCommand(
    Guid MatchId,
    string SeatNumber,
    string CardHolderName,
    string CardNumber,
    int ExpirationMonth,
    int ExpirationYear,
    string Cvv,
    Guid AttemptId = default) : IRequest<TicketDto>;
