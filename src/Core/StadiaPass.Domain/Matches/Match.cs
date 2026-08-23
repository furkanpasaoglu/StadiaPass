using StadiaPass.Domain.Common;
using StadiaPass.Domain.Matches.Events;

namespace StadiaPass.Domain.Matches;

public sealed class Match : AggregateRoot
{
    private Match()
    {
    }

    private Match(Guid id, string homeTeam, string awayTeam, string stadium, DateTimeOffset kickOffUtc, int capacity)
        : base(id)
    {
        HomeTeam = homeTeam;
        AwayTeam = awayTeam;
        Stadium = stadium;
        KickOffUtc = kickOffUtc;
        Capacity = capacity;
        Status = MatchStatus.Scheduled;
    }

    public string HomeTeam { get; private set; } = null!;

    public string AwayTeam { get; private set; } = null!;

    public string Stadium { get; private set; } = null!;

    public DateTimeOffset KickOffUtc { get; private set; }

    public int Capacity { get; private set; }

    public int IssuedTicketCount { get; private set; }

    public MatchStatus Status { get; private set; }

    public int RemainingCapacity => Capacity - IssuedTicketCount;

    public static Match Schedule(
        string homeTeam,
        string awayTeam,
        string stadium,
        DateTimeOffset kickOffUtc,
        int capacity,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
        {
            throw new DomainRuleViolationException(
                "Match.MissingTeam", "Both home and away teams are required.");
        }

        if (string.Equals(homeTeam, awayTeam, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "Match.SameTeam", "A team cannot play against itself.");
        }

        if (kickOffUtc <= now)
        {
            throw new DomainRuleViolationException(
                "Match.KickOffInPast", "Kick-off must be scheduled in the future.");
        }

        if (capacity <= 0)
        {
            throw new DomainRuleViolationException(
                "Match.InvalidCapacity", "Capacity must be greater than zero.");
        }

        var match = new Match(
            Guid.CreateVersion7(),
            homeTeam.Trim(),
            awayTeam.Trim(),
            stadium.Trim(),
            kickOffUtc,
            capacity);

        match.Raise(new MatchScheduledDomainEvent(match.Id, match.KickOffUtc));

        return match;
    }

    public void OpenSales()
    {
        if (Status is not MatchStatus.Scheduled)
        {
            throw new DomainRuleViolationException(
                "Match.NotSchedulable", $"Sales cannot be opened while the match is {Status}.");
        }

        Status = MatchStatus.OnSale;
    }

    public void Postpone(DateTimeOffset newKickOffUtc, DateTimeOffset now)
    {
        if (Status is MatchStatus.Played or MatchStatus.Cancelled)
        {
            throw new DomainRuleViolationException(
                "Match.NotPostponable", $"A {Status} match cannot be postponed.");
        }

        if (newKickOffUtc <= now)
        {
            throw new DomainRuleViolationException(
                "Match.KickOffInPast", "The new kick-off must be in the future.");
        }

        KickOffUtc = newKickOffUtc;
        Status = MatchStatus.Postponed;
        Raise(new MatchPostponedDomainEvent(Id, newKickOffUtc));
    }

    public void RegisterIssuedTicket()
    {
        EnsureTicketsCanBeIssued();

        IssuedTicketCount++;

        if (RemainingCapacity == 0)
        {
            Status = MatchStatus.SoldOut;
        }
    }

    public void EnsureTicketsCanBeIssued()
    {
        if (Status is not (MatchStatus.Scheduled or MatchStatus.OnSale))
        {
            throw new DomainRuleViolationException(
                "Match.SalesClosed", $"Tickets cannot be issued while the match is {Status}.");
        }

        if (RemainingCapacity <= 0)
        {
            throw new DomainRuleViolationException(
                "Match.CapacityExceeded", "The stadium capacity for this match is already reached.");
        }
    }
}
