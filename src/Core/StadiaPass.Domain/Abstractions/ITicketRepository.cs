using StadiaPass.Domain.Tickets;

namespace StadiaPass.Domain.Abstractions;

public interface ITicketRepository : IRepository<Ticket>
{
    Task<IReadOnlyList<Ticket>> GetByHolderAsync(string holderReference, CancellationToken cancellationToken = default);

    Task<Ticket?> GetBySeatAsync(Guid matchSeatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The live ticket a charge paid for. This is the only way back from a provider event - which knows a
    /// payment and nothing else - to the seat somebody is holding.
    /// </summary>
    Task<Ticket?> GetByPaymentIntentAsync(string paymentIntentId, CancellationToken cancellationToken = default);
}
