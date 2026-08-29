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
    /// <summary>
    /// The payment behind every ticket of this match that is still live.
    /// </summary>
    /// <remarks>
    /// Identifiers rather than aggregates: cancelling a sold-out fixture in a large venue would otherwise pull
    /// hundreds of tickets into one change tracker, and each one is settled in a scope of its own anyway.
    /// </remarks>
    Task<IReadOnlyList<string>> GetLivePaymentIntentsForMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken = default);

    Task<Ticket?> GetByPaymentIntentAsync(string paymentIntentId, CancellationToken cancellationToken = default);
}
