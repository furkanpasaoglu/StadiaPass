using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Domain.Abstractions;

public interface ITicketRepository : IRepository<Ticket>
{
    Task<bool> SeatIsTakenAsync(Guid matchId, SeatNumber seatNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Ticket>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
}
