using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.Tickets.Commands.ConfirmTicketPurchase;

internal sealed class ConfirmTicketPurchaseCommandHandler(
    IMatchRepository matchRepository,
    ITicketRepository ticketRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider) : IRequestHandler<ConfirmTicketPurchaseCommand, TicketDto>
{
    public async Task<TicketDto> Handle(ConfirmTicketPurchaseCommand request, CancellationToken cancellationToken)
    {
        var match = await matchRepository.GetWithSeatAsync(request.MatchId, request.SeatNumber, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        var now = dateTimeProvider.UtcNow;
        var seat = match.ConfirmSeatSale(request.SeatNumber, currentUser.Reference, now);
        var ticket = Ticket.IssueFor(match, seat, now);

        await ticketRepository.AddAsync(ticket, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ticket.ToDto();
    }
}
