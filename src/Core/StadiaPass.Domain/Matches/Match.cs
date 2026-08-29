using StadiaPass.Domain.Categories;
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
        CategoryId = category.Id;
        CategoryName = category.Name;
        VenueId = venueId;
        VenueName = venueName;
        HomeTeam = homeTeam;
        AwayTeam = awayTeam;
        KickOffUtc = kickOffUtc;
        Status = MatchStatus.OnSale;
    }

    public Guid CategoryId { get; private set; }

    /// <summary>Denormalised so a listing does not have to join the catalogue.</summary>
    public string CategoryName { get; private set; } = null!;

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

        category.EnsureCanBePlayedIn(venue);

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
        match.Raise(new MatchCreatedDomainEvent(match.Id, category.Name, venue.Id, match.Capacity));

        return match;
    }

    public MatchSeat ReserveSeat(string seatNumber, string holderReference, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(holderReference))
        {
            throw new DomainRuleViolationException(
                "Match.HolderRequired", "A holder reference is required to reserve a seat.");
        }

        EnsureSalesAreOpen(now);

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

    /// <summary>
    /// How many seats <see cref="ReserveSeat"/> would move out of the available column: one for a free seat,
    /// and none for a hold that has already run out and is about to be taken over, because that seat is
    /// counted as reserved already and only changes hands.
    /// </summary>
    /// <remarks>
    /// Ask before the transition, not after - afterwards the answer is always zero. It exists because the
    /// counters belong to the database rather than to whatever totals this request happened to read, and the
    /// update that writes them has to be told how far to move.
    /// </remarks>
    public int SeatsClaimedByReserving(string seatNumber) =>
        RequireSeat(seatNumber).Status is SeatStatus.Reserved ? 0 : 1;

    /// <summary>
    /// Runs every rule <see cref="ConfirmSeatSale"/> would run and changes nothing, returning the seat so a
    /// caller can read its price. Taking money for a seat that cannot be sold - one held by somebody else,
    /// or a hold that has already run out - is far worse than refusing before the card is charged.
    /// </summary>
    public MatchSeat EnsureSeatCanBeSoldTo(string seatNumber, string holderReference, DateTimeOffset now)
    {
        EnsureSalesAreOpen(now);

        var seat = RequireSeat(seatNumber);

        seat.EnsureCanBeSoldTo(holderReference, now);

        return seat;
    }

    public MatchSeat ConfirmSeatSale(string seatNumber, string holderReference, DateTimeOffset now)
    {
        EnsureSalesAreOpen(now);

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

    /// <summary>
    /// Takes a completed sale back and puts the seat on offer again. A chargeback or a refund issued from the
    /// provider's dashboard arrives long after the request that made the sale, so this deliberately does not
    /// ask whether sales are open: refusing to void a seat on a postponed or finished match would leave the
    /// money returned and the seat still marked sold, which is the worst of both.
    /// </summary>
    public MatchSeat VoidSeatSale(string seatNumber, DateTimeOffset now)
    {
        var seat = RequireSeat(seatNumber);

        seat.VoidSale();

        SoldSeatCount--;
        AvailableSeatCount++;

        if (Status is MatchStatus.SoldOut)
        {
            Status = MatchStatus.OnSale;
        }

        Raise(new SeatSaleVoidedDomainEvent(Id, seat.Id, seat.SeatNumber.ToString(), now));

        return seat;
    }

    /// <summary>
    /// Calls the fixture off: nothing more can be sold, and nobody is left holding a seat.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sold seats are deliberately left exactly as they are. Each one owes somebody their money back, and a
    /// refund that the provider refuses has to be able to be retried on its own rather than taking a whole
    /// fixture's worth of them down with it, so they are settled one ticket at a time off the broker. Freeing
    /// them here would throw away the only record of what still has to be paid.
    /// </para>
    /// <para>
    /// Only the seats that are loaded can be given back. A caller that reads the fixture without its held
    /// seats will cancel it and leave those holds sitting in the database, counted against a match nobody can
    /// buy from and untouched by the sweeper for the rest of their ten minutes.
    /// </para>
    /// </remarks>
    public void Cancel(DateTimeOffset now)
    {
        if (Status is MatchStatus.Cancelled)
        {
            throw new DomainRuleViolationException(
                "Match.AlreadyCancelled", "This match has already been cancelled.");
        }

        if (now >= KickOffUtc)
        {
            throw new DomainRuleViolationException(
                "Match.AlreadyKickedOff",
                "A match that has already kicked off cannot be cancelled.");
        }

      
        foreach (var seatNumber in _seats
                     .Where(seat => seat.Status is SeatStatus.Reserved)
                     .Select(seat => seat.SeatNumber.ToString())
                     .ToArray())
        {
            ReleaseSeat(seatNumber, now);
        }

        Status = MatchStatus.Cancelled;

        Raise(new MatchCancelledDomainEvent(Id, now));
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

    /// <summary>
    /// The two conditions a seat may be taken under: the fixture is selling, and it has not started.
    /// </summary>
    /// <remarks>
    /// The clock half is not decoration. The listing hides a fixture once its kick-off has passed, but the
    /// seat map is fetched by identifier and filters by nothing, so a link that outlived its match - a
    /// bookmark, a shared URL, a document still in the search index - reached a page that would happily take
    /// a hold and then a payment for a match already played.
    /// <para>
    /// A time rather than a status, deliberately. Marking fixtures finished would need something to run
    /// around doing the marking, and the door would stand open for however long that thing was late or
    /// stopped. The kick-off is already written on the fixture and needs nobody's help to arrive.
    /// </para>
    /// <para>
    /// Only the ways *into* a seat ask this. <see cref="VoidSeatSale"/> and <see cref="ReleaseSeat"/> take a
    /// seat back and must keep working afterwards: a chargeback arrives long after the sale that caused it,
    /// and the sweeper that returns abandoned holds runs on a timer rather than on the fixture list.
    /// </para>
    /// </remarks>
    private void EnsureSalesAreOpen(DateTimeOffset now)
    {
        if (Status is not MatchStatus.OnSale)
        {
            throw new DomainRuleViolationException(
                "Match.SalesClosed", $"Seats cannot be traded while the match is {Status}.");
        }

        if (now >= KickOffUtc)
        {
            throw new DomainRuleViolationException(
                "Match.SalesClosed", "Seats cannot be traded for a match that has already kicked off.");
        }
    }
}
