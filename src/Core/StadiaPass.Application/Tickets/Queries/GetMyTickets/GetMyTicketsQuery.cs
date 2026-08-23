using MediatR;

namespace StadiaPass.Application.Tickets.Queries.GetMyTickets;

public sealed record GetMyTicketsQuery : IRequest<IReadOnlyList<TicketDto>>;
