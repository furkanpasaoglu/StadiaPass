using MediatR;

namespace StadiaPass.Application.Tickets.Commands.ConfirmTicketSale;

public sealed record ConfirmTicketSaleCommand(Guid TicketId) : IRequest<TicketDto>;
