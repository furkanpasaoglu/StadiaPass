using StadiaPass.Domain.Tickets;

namespace StadiaPass.Domain.Abstractions;

public interface ITicketRepository : IRepository<Ticket>
{
    Task<IReadOnlyList<Ticket>> GetByHolderAsync(string holderReference, CancellationToken cancellationToken = default);

    Task<Ticket?> GetBySeatAsync(Guid matchSeatId, CancellationToken cancellationToken = default);
}
