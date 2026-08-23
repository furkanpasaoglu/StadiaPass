using Microsoft.EntityFrameworkCore;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Persistence.Repositories;

internal sealed class TicketRepository(StadiaPassDbContext context)
    : Repository<Ticket>(context), ITicketRepository
{
    public Task<bool> SeatIsTakenAsync(
        Guid matchId,
        SeatNumber seatNumber,
        CancellationToken cancellationToken = default) =>
        Set.AnyAsync(
            ticket => ticket.MatchId == matchId && ticket.SeatNumber == seatNumber,
            cancellationToken);

    public async Task<IReadOnlyList<Ticket>> GetByMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(ticket => ticket.MatchId == matchId)
            .OrderBy(ticket => ticket.SeatNumber)
            .ToListAsync(cancellationToken);
}
