using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Tickets.Commands.ReserveSeat;

internal sealed class ReserveSeatCommandHandler(
    IMatchRepository matchRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider) : IRequestHandler<ReserveSeatCommand, SeatReservationDto>
{
    public async Task<SeatReservationDto> Handle(ReserveSeatCommand request, CancellationToken cancellationToken)
    {
        var match = await matchRepository.GetWithSeatAsync(request.MatchId, request.SeatNumber, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        var seat = match.ReserveSeat(request.SeatNumber, currentUser.Reference, dateTimeProvider.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return seat.ToReservationDto(match.Id);
    }
}
