using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.Tickets.Commands.ConfirmTicketSale;

internal sealed class ConfirmTicketSaleCommandHandler(
    ITicketRepository ticketRepository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : IRequestHandler<ConfirmTicketSaleCommand, TicketDto>
{
    public async Task<TicketDto> Handle(ConfirmTicketSaleCommand request, CancellationToken cancellationToken)
    {
        var ticket = await ticketRepository.GetByIdAsync(request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        ticket.ConfirmSale(dateTimeProvider.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ticket.ToDto();
    }
}
