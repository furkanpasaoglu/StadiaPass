using Microsoft.EntityFrameworkCore;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Persistence.Repositories;

internal sealed class TicketRepository(StadiaPassDbContext context)
    : Repository<Ticket>(context), ITicketRepository
{
    public async Task<IReadOnlyList<Ticket>> GetByHolderAsync(
        string holderReference,
        CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(ticket => ticket.HolderReference == holderReference)
            .OrderByDescending(ticket => ticket.IssuedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<Ticket?> GetBySeatAsync(Guid matchSeatId, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(
            ticket => ticket.MatchSeatId == matchSeatId && ticket.Status == TicketStatus.Issued,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetLivePaymentIntentsForMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(ticket => ticket.MatchId == matchId && ticket.Status == TicketStatus.Issued)
            .Select(ticket => ticket.PaymentIntentId)
            .ToListAsync(cancellationToken);

    public Task<Ticket?> GetByPaymentIntentAsync(
        string paymentIntentId,
        CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(
            ticket => ticket.PaymentIntentId == paymentIntentId && ticket.Status == TicketStatus.Issued,
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// One grouped aggregate: the answer is a handful of rows and the tickets themselves never leave the
    /// database. Grouping by currency as well as status costs nothing today - a fixture is priced in one
    /// currency - and means that the day a second one appears the totals arrive separated rather than
    /// silently added together.
    /// </remarks>
    public async Task<IReadOnlyList<TicketRevenueLine>> GetRevenueLinesForMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(ticket => ticket.MatchId == matchId)
            .GroupBy(ticket => new { ticket.Status, ticket.Price.Currency })
            .Select(group => new TicketRevenueLine(
                group.Key.Status,
                group.Key.Currency,
                group.Count(),
                group.Sum(ticket => ticket.Price.Amount)))
            .ToListAsync(cancellationToken);
}
