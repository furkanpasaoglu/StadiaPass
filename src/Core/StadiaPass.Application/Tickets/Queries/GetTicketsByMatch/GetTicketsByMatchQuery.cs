using MediatR;

namespace StadiaPass.Application.Tickets.Queries.GetTicketsByMatch;

public sealed record GetTicketsByMatchQuery(Guid MatchId) : IRequest<IReadOnlyList<TicketDto>>;
