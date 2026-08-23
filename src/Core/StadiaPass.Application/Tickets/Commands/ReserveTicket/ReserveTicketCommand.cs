using MediatR;

namespace StadiaPass.Application.Tickets.Commands.ReserveTicket;

public sealed record ReserveTicketCommand(Guid TicketId, string HolderReference) : IRequest<TicketDto>;
