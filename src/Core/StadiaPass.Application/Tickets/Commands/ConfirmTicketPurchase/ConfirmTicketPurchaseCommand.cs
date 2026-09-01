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
/// that sends nothing lands on <see cref="Guid.Empty"/> and is given an id by the handler, which costs it
/// double-click protection and nothing else. Deliberately no <c>= default</c> on the parameter: the CLR
/// cannot store a struct default in metadata, and the OpenAPI schema exporter trips over the null it finds
/// there - which took the whole /openapi/v1.json document down with a 500. Omitting the default changes
/// nothing on the wire; a missing JSON property already deserializes to <see cref="Guid.Empty"/>.
/// </param>
public sealed record ConfirmTicketPurchaseCommand(
    Guid MatchId,
    string SeatNumber,
    string CardHolderName,
    string CardNumber,
    int ExpirationMonth,
    int ExpirationYear,
    string Cvv,
    Guid AttemptId) : IRequest<TicketDto>;
