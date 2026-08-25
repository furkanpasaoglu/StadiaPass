using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Tickets.Commands.ReserveSeat;

/// <summary>
/// Puts a hold on a seat for ten minutes.
/// </summary>
/// <remarks>
/// No distributed lock here, unlike a purchase. That lock exists to keep two people from being charged for
/// the same seat; a hold takes no money, so the seat's own concurrency token is enough - the second writer
/// loses, is told so, and has lost nothing but a moment.
/// </remarks>
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

        // Asked before the transition, because afterwards the seat is reserved either way and the answer is
        // always zero. Taking over a hold that had already run out moves no counters.
        var claimed = match.SeatsClaimedByReserving(request.SeatNumber);

        // Outside the transaction on purpose. Everything in here changes memory only, and the retrying
        // execution strategy runs the delegate below again after a transient failure - a second pass over
        // this line would find the seat it just reserved and refuse it, turning a blink of the network into
        // a permanent error. Nothing is written until the save inside.
        var seat = match.ReserveSeat(request.SeatNumber, currentUser.Reference, dateTimeProvider.UtcNow);

        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // Coarsest row first, exactly as a sale, a void and the sweeper do it, so no two of them can
                // ever reach for the match and a seat in opposite orders.
                await matchRepository.ApplySeatReservationToCountersAsync(match, claimed, token);

                await unitOfWork.SaveChangesAsync(token);
            },
            cancellationToken);

        return seat.ToReservationDto(match.Id);
    }
}
