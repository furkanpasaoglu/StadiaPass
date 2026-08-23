using MediatR;
using StadiaPass.Domain.Common.ValueObjects;

namespace StadiaPass.Application.Tickets.Commands.CreateTicket;

public sealed record CreateTicketCommand(
    Guid MatchId,
    string Block,
    int Row,
    int Number,
    decimal Price,
    string Currency = Money.DefaultCurrency) : IRequest<TicketDto>;
