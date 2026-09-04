using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.Matches.Queries.GetMatchRevenue;

/// <summary>
/// The one rule worth writing down here: <b>a refunded ticket is not revenue.</b> It lives in this handler,
/// in code that a test can break, rather than in a sentence somebody hopes a language model will remember.
/// The analytics tool an AI client calls goes through this method like every other caller does.
/// </summary>
internal sealed class GetMatchRevenueQueryHandler(
    IMatchRepository matchRepository,
    ITicketRepository ticketRepository) : IRequestHandler<GetMatchRevenueQuery, MatchRevenueDto>
{
    public async Task<MatchRevenueDto> Handle(
        GetMatchRevenueQuery request,
        CancellationToken cancellationToken)
    {
        var match = await matchRepository.GetByIdAsync(request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        var lines = await ticketRepository.GetRevenueLinesForMatchAsync(request.MatchId, cancellationToken);

        var sold = Total(lines, TicketStatus.Issued);
        var refunded = Total(lines, TicketStatus.Cancelled);

        return new MatchRevenueDto(
            match.Id,
            match.HomeTeam,
            match.AwayTeam,
            match.KickOffUtc,
            match.Status.ToString(),
            sold.Currency,
            TicketsSold: Count(lines, TicketStatus.Issued),
            NetRevenue: sold.Amount,
            TicketsRefunded: Count(lines, TicketStatus.Cancelled),
            RefundedAmount: refunded.Amount,
            match.Capacity,
            match.SoldSeatCount,
            OccupancyPercent: Occupancy(match));
    }

    /// <summary>
    /// Adds the lines of one state together through <see cref="Money"/>, which refuses to add two
    /// currencies. A fixture is priced in one currency today, so this never fires - and if it ever does,
    /// failing is the correct answer: 300 TRY and 100 EUR do not make 400 of anything, and a total that
    /// says they do is worse than no total at all.
    /// </summary>
    private static Money Total(IReadOnlyList<TicketRevenueLine> lines, TicketStatus status) =>
        lines
            .Where(line => line.Status == status)
            .Aggregate(
                Money.Zero(),
                (running, line) => running.Add(Money.Create(line.Amount, line.Currency)));

    private static int Count(IReadOnlyList<TicketRevenueLine> lines, TicketStatus status) =>
        lines.Where(line => line.Status == status).Sum(line => line.Count);

    /// <summary>
    /// Sold seats against capacity, from the fixture's own counters rather than from the ticket rows. The
    /// counters are what the seat map and the listing show, so a percentage taken from anywhere else could
    /// disagree with the screen the same person is looking at. Held seats are not sold and do not count.
    /// </summary>
    private static decimal Occupancy(Match match) =>
        match.Capacity is 0
            ? decimal.Zero
            : decimal.Round(match.SoldSeatCount * 100m / match.Capacity, 2, MidpointRounding.ToEven);
}
