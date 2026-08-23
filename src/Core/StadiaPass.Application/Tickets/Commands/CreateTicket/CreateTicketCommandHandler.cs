using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.Tickets.Commands.CreateTicket;

internal sealed class CreateTicketCommandHandler(
    IMatchRepository matchRepository,
    ITicketRepository ticketRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateTicketCommand, TicketDto>
{
    public async Task<TicketDto> Handle(CreateTicketCommand request, CancellationToken cancellationToken)
    {
        var match = await matchRepository.GetByIdAsync(request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        match.EnsureTicketsCanBeIssued();

        var seatNumber = SeatNumber.Create(request.Block, request.Row, request.Number);

        if (await ticketRepository.SeatIsTakenAsync(match.Id, seatNumber, cancellationToken))
        {
            throw new ConflictException($"Seat {seatNumber} has already been issued for this match.");
        }

        var ticket = Ticket.Issue(match.Id, seatNumber, Money.Create(request.Price, request.Currency));

        await ticketRepository.AddAsync(ticket, cancellationToken);
        match.RegisterIssuedTicket();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ticket.ToDto();
    }
}
