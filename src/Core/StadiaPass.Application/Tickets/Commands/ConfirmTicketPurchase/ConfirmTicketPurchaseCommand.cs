using MediatR;

namespace StadiaPass.Application.Tickets.Commands.ConfirmTicketPurchase;

/// <summary>Turns the caller's own seat reservation into a sale and issues the ticket.</summary>
public sealed record ConfirmTicketPurchaseCommand(Guid MatchId, string SeatNumber) : IRequest<TicketDto>;
