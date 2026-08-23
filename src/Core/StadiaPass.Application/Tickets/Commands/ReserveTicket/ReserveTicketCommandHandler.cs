using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.Tickets.Commands.ReserveTicket;

internal sealed class ReserveTicketCommandHandler(
    ITicketRepository ticketRepository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : IRequestHandler<ReserveTicketCommand, TicketDto>
{
    public async Task<TicketDto> Handle(ReserveTicketCommand request, CancellationToken cancellationToken)
    {
        var ticket = await ticketRepository.GetByIdAsync(request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        ticket.Reserve(request.HolderReference, dateTimeProvider.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ticket.ToDto();
    }
}
