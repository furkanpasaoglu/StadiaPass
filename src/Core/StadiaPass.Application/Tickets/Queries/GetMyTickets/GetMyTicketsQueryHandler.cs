using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Tickets.Queries.GetMyTickets;

/// <summary>
/// Everything this account has bought, newest first, each one against the fixture it is for.
/// </summary>
/// <remarks>
/// The fixtures are read in one go rather than one per ticket. Somebody holding a season's worth of stubs
/// would otherwise cost a round trip each, and most of them name the same handful of matches anyway.
/// </remarks>
internal sealed class GetMyTicketsQueryHandler(
    ITicketRepository ticketRepository,
    IMatchRepository matchRepository,
    ICurrentUser currentUser) : IRequestHandler<GetMyTicketsQuery, IReadOnlyList<MyTicketDto>>
{
    public async Task<IReadOnlyList<MyTicketDto>> Handle(
        GetMyTicketsQuery request,
        CancellationToken cancellationToken)
    {
        var tickets = await ticketRepository.GetByHolderAsync(currentUser.Reference, cancellationToken);

        if (tickets.Count is 0)
        {
            return [];
        }

        var matches = await matchRepository.GetByIdsAsync(
            [.. tickets.Select(ticket => ticket.MatchId).Distinct()],
            cancellationToken);

        var matchesById = matches.ToDictionary(match => match.Id);

        return
        [
            .. tickets.Select(ticket => ticket.ToMyTicketDto(matchesById.GetValueOrDefault(ticket.MatchId)))
        ];
    }
}

internal static class MyTicketMapping
{
    /// <param name="match">
    /// The fixture, when it is still there. A ticket outliving its match is not something a holder should be
    /// shown an error for, so the stub keeps its seat, its price and its code and simply says nothing about
    /// a fixture nobody can look up.
    /// </param>
    public static MyTicketDto ToMyTicketDto(this Domain.Tickets.Ticket ticket, Match? match) => new(
        ticket.Id,
        ticket.MatchId,
        ticket.SeatNumber.ToString(),
        ticket.Price.Amount,
        ticket.Price.Currency,
        ticket.AccessCode,
        ticket.IssuedAtUtc,
        ticket.Status.ToString(),
        match?.HomeTeam ?? string.Empty,
        match?.AwayTeam ?? string.Empty,
        match?.VenueName ?? string.Empty,
        match?.KickOffUtc ?? ticket.IssuedAtUtc,
        match?.Status.ToString() ?? string.Empty);
}
