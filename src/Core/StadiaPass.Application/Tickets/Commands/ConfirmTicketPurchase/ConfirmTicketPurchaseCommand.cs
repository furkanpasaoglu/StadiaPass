using MediatR;

namespace StadiaPass.Application.Tickets.Commands.ConfirmTicketPurchase;

/// <summary>
/// Turns the caller's own seat reservation into a sale and issues the ticket, once the card has been charged.
/// The card details are used for that single charge and are never stored; the number and the security code
/// are masked before any log event is written.
/// </summary>
public sealed record ConfirmTicketPurchaseCommand(
    Guid MatchId,
    string SeatNumber,
    string CardHolderName,
    string CardNumber,
    int ExpirationMonth,
    int ExpirationYear,
    string Cvv) : IRequest<TicketDto>;
