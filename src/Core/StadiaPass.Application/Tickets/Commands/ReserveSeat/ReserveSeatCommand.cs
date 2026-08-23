using MediatR;

namespace StadiaPass.Application.Tickets.Commands.ReserveSeat;

/// <summary>
/// Customer picks a seat on the map. The holder is taken from the signed-in caller, never from the request,
/// so a seat cannot be held in somebody else's name.
/// </summary>
public sealed record ReserveSeatCommand(Guid MatchId, string SeatNumber) : IRequest<SeatReservationDto>;
