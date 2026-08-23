using StadiaPass.Domain.Common;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches.Events;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Domain.Matches;

/// <summary>
/// Aggregate root for a fixture and its seat map. Seats are materialised from the venue plan at creation
/// time and every seat transition goes through this class, which keeps the seat counters and the match
/// status consistent with the individual seats.
/// </summary>
public sealed class Match : AggregateRoot
{
    public static readonly TimeSpan ReservationWindow = TimeSpan.FromMinutes(10);

    private readonly List<MatchSeat> _seats = [];

    private Match()
    {
    }

    private Match(
        Guid id,
        SportCategory category,
        Guid venueId,
        string venueName,
        string homeTeam,
        string awayTeam,
        DateTimeOffset kickOffUtc)
        : base(id)
    {
        Category = category;
        VenueId = venueId;
        VenueName = venueName;
        HomeTeam = homeTeam;
        AwayTeam = awayTeam;
        KickOffUtc = kickOffUtc;
        Status = MatchStatus.OnSale;
    }

    public SportCategory Category { get; private set; }

    public Guid VenueId { get; private set; }

    /// <summary>Denormalised for listing screens; the venue plan itself stays in the venue aggregate.</summary>
    public string VenueName { get; private set; } = null!;

    public string HomeTeam { get; private set; } = null!;

    public string AwayTeam { get; private set; } = null!;

    public DateTimeOffset KickOffUtc { get; private set; }

    public MatchStatus Status { get; private set; }

    public int Capacity { get; private set; }

    public int AvailableSeatCount { get; private set; }

    public int ReservedSeatCount { get; private set; }

    public int SoldSeatCount { get; private set; }

    /// <summary>Only populated when the seat map is explicitly loaded; list queries use the counters.</summary>
    public IReadOnlyCollection<MatchSeat> Seats => _seats.AsReadOnly();

    public static Match Create(
        SportCategory category,
        Venue venue,
        string homeTeam,
        string awayTeam,
        DateTimeOffset kickOffUtc,
        Money basePrice,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
        {
            throw new DomainRuleViolationException(
                "Match.MissingTeam", "Both home and away teams are required.");
        }

        if (string.Equals(homeTeam.Trim(), awayTeam.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "Match.SameTeam", "A team cannot play against itself.");
        }

        if (kickOffUtc <= now)
        {
            throw new DomainRuleViolationException(
                "Match.KickOffInPast", "Kick-off must be scheduled in the future.");
        }

        if (!venue.CanHost(category))
        {
            throw new DomainRuleViolationException(
                "Match.VenueCannotHostCategory",
                $"{venue.Name} is a {venue.Kind} and cannot host {category}.");
        }

        if (basePrice.Amount <= 0)
        {
            throw new DomainRuleViolationException(
                "Match.InvalidBasePrice", "Base ticket price must be greater than zero.");
        }

        var match = new Match(
            Guid.CreateVersion7(),
            category,
            venue.Id,
            venue.Name,
            homeTeam.Trim(),
            awayTeam.Trim(),
            kickOffUtc.ToUniversalTime());

        match.MaterialiseSeats(venue, basePrice);
        match.Raise(new MatchCreatedDomainEvent(match.Id, category.ToString(), venue.Id, match.Capacity));

        return match;
    }

    public MatchSeat ReserveSeat(string seatNumber, string holderReference, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(holderReference))
        {
            throw new DomainRuleViolationException(
                "Match.HolderRequired", "A holder reference is required to reserve a seat.");
        }

        EnsureSalesAreOpen();

        var seat = RequireSeat(seatNumber);
        var wasReserved = seat.Status is SeatStatus.Reserved;

        seat.Reserve(holderReference.Trim(), now, ReservationWindow);

        if (!wasReserved)
        {
            AvailableSeatCount--;
            ReservedSeatCount++;
        }

        Raise(new SeatReservedDomainEvent(
            Id, seat.Id, seat.SeatNumber.ToString(), holderReference.Trim(), seat.ReservationExpiresAtUtc!.Value));

        return seat;
    }

    public MatchSeat ConfirmSeatSale(string seatNumber, string holderReference, DateTimeOffset now)
    {
        EnsureSalesAreOpen();

        var seat = RequireSeat(seatNumber);

        seat.ConfirmSale(holderReference, now);

        ReservedSeatCount--;
        SoldSeatCount++;

        if (AvailableSeatCount is 0 && ReservedSeatCount is 0)
        {
            Status = MatchStatus.SoldOut;
        }

        Raise(new SeatSoldDomainEvent(Id, seat.Id, seat.SeatNumber.ToString(), holderReference, seat.Price.Amount));

        return seat;
    }

    public void ReleaseSeat(string seatNumber, DateTimeOffset now)
    {
        var seat = RequireSeat(seatNumber);

        if (seat.Status is not SeatStatus.Reserved)
        {
            throw new DomainRuleViolationException(
                "Match.SeatNotReserved", $"Seat {seat.SeatNumber} is {seat.Status} and has no reservation to release.");
        }

        seat.Release();

        ReservedSeatCount--;
        AvailableSeatCount++;

        if (Status is MatchStatus.SoldOut)
        {
            Status = MatchStatus.OnSale;
        }

        Raise(new SeatReleasedDomainEvent(Id, seat.Id, seat.SeatNumber.ToString(), now));
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

        KickOffUtc = newKickOffUtc.ToUniversalTime();
        Status = MatchStatus.Postponed;
        Raise(new MatchPostponedDomainEvent(Id, newKickOffUtc));
    }

    private void MaterialiseSeats(Venue venue, Money basePrice)
    {
        foreach (var block in venue.Blocks)
        {
            var blockPrice = basePrice.Amount * block.PriceMultiplier;

            for (var row = 1; row <= block.RowCount; row++)
            {
                for (var number = 1; number <= block.SeatsPerRow; number++)
                {
                    // A fresh Money per seat: an owned value object instance must never be shared
                    // between two owners, or EF Core cannot track it.
                    _seats.Add(MatchSeat.Materialise(
                        SeatNumber.Create(block.Name, row, number),
                        Money.Create(blockPrice, basePrice.Currency)));
                }
            }
        }

        Capacity = _seats.Count;
        AvailableSeatCount = _seats.Count;
    }

    private MatchSeat RequireSeat(string seatNumber)
    {
        var parsed = SeatNumber.Parse(seatNumber);

        return _seats.SingleOrDefault(seat => seat.SeatNumber == parsed)
               ?? throw new DomainRuleViolationException(
                   "Match.SeatNotFound", $"Seat {parsed} does not exist in this match.");
    }

    private void EnsureSalesAreOpen()
    {
        if (Status is not MatchStatus.OnSale)
        {
            throw new DomainRuleViolationException(
                "Match.SalesClosed", $"Seats cannot be traded while the match is {Status}.");
        }
    }
}
