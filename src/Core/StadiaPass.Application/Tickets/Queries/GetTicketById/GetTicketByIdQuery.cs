using MediatR;

namespace StadiaPass.Application.Tickets.Queries.GetTicketById;

public sealed record GetTicketByIdQuery(Guid TicketId) : IRequest<TicketDto>;
