using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Application.Tickets.Queries.GetMyTickets;

internal sealed class GetMyTicketsQueryHandler(ITicketRepository ticketRepository, ICurrentUser currentUser)
    : IRequestHandler<GetMyTicketsQuery, IReadOnlyList<TicketDto>>
{
    public async Task<IReadOnlyList<TicketDto>> Handle(
        GetMyTicketsQuery request,
        CancellationToken cancellationToken)
    {
        var tickets = await ticketRepository.GetByHolderAsync(currentUser.Reference, cancellationToken);

        return [.. tickets.Select(ticket => ticket.ToDto())];
    }
}
