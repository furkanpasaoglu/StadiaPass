using StadiaPass.Domain.Common;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Tickets.Events;

namespace StadiaPass.Domain.Tickets;

public sealed class Ticket : AggregateRoot
{
    public static readonly TimeSpan ReservationWindow = TimeSpan.FromMinutes(15);

    private Ticket()
    {
    }

    private Ticket(Guid id, Guid matchId, SeatNumber seatNumber, Money price)
        : base(id)
    {
        MatchId = matchId;
        SeatNumber = seatNumber;
        Price = price;
        Status = TicketStatus.Available;
    }

    public Guid MatchId { get; private set; }

    public SeatNumber SeatNumber { get; private set; } = null!;

    public Money Price { get; private set; } = null!;

    public TicketStatus Status { get; private set; }

    public string? HolderReference { get; private set; }

    public DateTimeOffset? ReservedAtUtc { get; private set; }

    public DateTimeOffset? ReservationExpiresAtUtc { get; private set; }

    public DateTimeOffset? SoldAtUtc { get; private set; }

    public static Ticket Issue(Guid matchId, SeatNumber seatNumber, Money price)
    {
        if (matchId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "Ticket.MatchRequired", "A ticket must belong to a match.");
        }

        if (price.Amount <= 0)
        {
            throw new DomainRuleViolationException(
                "Ticket.InvalidPrice", "Ticket price must be greater than zero.");
        }

        var ticket = new Ticket(Guid.CreateVersion7(), matchId, seatNumber, price);
        ticket.Raise(new TicketIssuedDomainEvent(ticket.Id, matchId, seatNumber.ToString(), price.Amount));

        return ticket;
    }

    public void Reserve(string holderReference, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(holderReference))
        {
            throw new DomainRuleViolationException(
                "Ticket.HolderRequired", "A holder reference is required to reserve a ticket.");
        }

        if (Status is not TicketStatus.Available)
        {
            throw new DomainRuleViolationException(
                "Ticket.NotReservable", $"Seat {SeatNumber} is {Status} and cannot be reserved.");
        }

        Status = TicketStatus.Reserved;
        HolderReference = holderReference.Trim();
        ReservedAtUtc = now;
        ReservationExpiresAtUtc = now.Add(ReservationWindow);

        Raise(new TicketReservedDomainEvent(Id, MatchId, HolderReference, ReservationExpiresAtUtc.Value));
    }

    public void ConfirmSale(DateTimeOffset now)
    {
        if (Status is not TicketStatus.Reserved)
        {
            throw new DomainRuleViolationException(
                "Ticket.NotReserved", $"Only a reserved ticket can be sold. Seat {SeatNumber} is {Status}.");
        }

        if (ReservationExpiresAtUtc is { } expiry && expiry < now)
        {
            throw new DomainRuleViolationException(
                "Ticket.ReservationExpired", $"The reservation for seat {SeatNumber} expired at {expiry:u}.");
        }

        Status = TicketStatus.Sold;
        SoldAtUtc = now;
        ReservationExpiresAtUtc = null;

        Raise(new TicketSoldDomainEvent(Id, MatchId, HolderReference!, Price.Amount, now));
    }

    public void ReleaseReservation(DateTimeOffset now)
    {
        if (Status is not TicketStatus.Reserved)
        {
            throw new DomainRuleViolationException(
                "Ticket.NotReserved", $"Seat {SeatNumber} is {Status} and has no reservation to release.");
        }

        Status = TicketStatus.Available;
        HolderReference = null;
        ReservedAtUtc = null;
        ReservationExpiresAtUtc = null;

        Raise(new TicketReservationReleasedDomainEvent(Id, MatchId, now));
    }

    public bool IsReservationExpired(DateTimeOffset now) =>
        Status is TicketStatus.Reserved && ReservationExpiresAtUtc < now;
}
