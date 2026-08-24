using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Tickets;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.Tickets.Queries.GetTicketById;

internal sealed class GetTicketByIdQueryHandler(ITicketRepository ticketRepository, ICurrentUser currentUser)
    : IRequestHandler<GetTicketByIdQuery, TicketDto>
{
    public async Task<TicketDto> Handle(GetTicketByIdQuery request, CancellationToken cancellationToken)
    {
        var ticket = await ticketRepository.GetByIdAsync(request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        if (!CanBeReadByCaller(ticket))
        {
            // Answer as if it did not exist. A ticket carries the seat, the price and the buyer, and
            // "forbidden" would confirm to a stranger that the id they guessed is a real ticket.
            throw new NotFoundException(nameof(Ticket), request.TicketId);
        }

        return ticket.ToDto();
    }

    /// <summary>
    /// Everyone who can reach this endpoint holds Tickets.View, which is what a customer needs to open
    /// their own ticket. Reading somebody else's takes the box-office permission on top.
    /// </summary>
    private bool CanBeReadByCaller(Ticket ticket) =>
        string.Equals(ticket.HolderReference, currentUser.Reference, StringComparison.Ordinal)
        || currentUser.HasPermission(StadiaPassPermissions.Tickets.ViewAll);
}
