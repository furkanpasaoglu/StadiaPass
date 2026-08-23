using MediatR;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Application.Tickets.Queries.GetTicketsByMatch;

internal sealed class GetTicketsByMatchQueryHandler(ITicketRepository ticketRepository)
    : IRequestHandler<GetTicketsByMatchQuery, IReadOnlyList<TicketDto>>
{
    public async Task<IReadOnlyList<TicketDto>> Handle(
        GetTicketsByMatchQuery request,
        CancellationToken cancellationToken)
    {
        var tickets = await ticketRepository.GetByMatchAsync(request.MatchId, cancellationToken);

        return [.. tickets.Select(ticket => ticket.ToDto())];
    }
}
