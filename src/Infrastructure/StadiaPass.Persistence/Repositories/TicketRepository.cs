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
}
