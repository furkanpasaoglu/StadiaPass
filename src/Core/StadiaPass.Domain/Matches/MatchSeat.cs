using StadiaPass.Domain.Common;
using StadiaPass.Domain.Common.ValueObjects;

namespace StadiaPass.Domain.Matches;

/// <summary>
/// A single seat of a match, materialised from the venue plan when the match is created. It is an entity
/// inside the <see cref="Match"/> aggregate: every transition is <c>internal</c>, so a seat can only ever be
/// moved by its match and the aggregate invariants cannot be bypassed.
/// </summary>
public sealed class MatchSeat : Entity
{
    private MatchSeat()
    {
    }

    private MatchSeat(Guid id, SeatNumber seatNumber, Money price)
        : base(id)
    {
        SeatNumber = seatNumber;
        Price = price;
        Status = SeatStatus.Available;
    }

    public SeatNumber SeatNumber { get; private set; } = null!;

    public Money Price { get; private set; } = null!;

    public SeatStatus Status { get; private set; }

    public string? HolderReference { get; private set; }

    public DateTimeOffset? ReservedAtUtc { get; private set; }

    public DateTimeOffset? ReservationExpiresAtUtc { get; private set; }

    public DateTimeOffset? SoldAtUtc { get; private set; }

    internal static MatchSeat Materialise(SeatNumber seatNumber, Money price) =>
        new(Guid.CreateVersion7(), seatNumber, price);

    internal void Reserve(string holderReference, DateTimeOffset now, TimeSpan window)
    {
        if (Status is SeatStatus.Reserved && IsReservationExpired(now))
        {
            Release();
        }

        if (Status is not SeatStatus.Available)
        {
            throw new DomainRuleViolationException(
                "Seat.NotAvailable", $"Seat {SeatNumber} is {Status} and cannot be reserved.");
        }

        Status = SeatStatus.Reserved;
        HolderReference = holderReference;
        ReservedAtUtc = now;
        ReservationExpiresAtUtc = now.Add(window);
    }

    internal void ConfirmSale(string holderReference, DateTimeOffset now)
    {
        if (Status is not SeatStatus.Reserved)
        {
            throw new DomainRuleViolationException(
                "Seat.NotReserved", $"Seat {SeatNumber} is {Status}; only a reserved seat can be sold.");
        }

        if (!string.Equals(HolderReference, holderReference, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                "Seat.ReservedByAnotherHolder", $"Seat {SeatNumber} is reserved by someone else.");
        }

        if (IsReservationExpired(now))
        {
            throw new DomainRuleViolationException(
                "Seat.ReservationExpired",
                $"The reservation for seat {SeatNumber} expired at {ReservationExpiresAtUtc:u}.");
        }

        Status = SeatStatus.Sold;
        SoldAtUtc = now;
        ReservationExpiresAtUtc = null;
    }

    internal void Release()
    {
        Status = SeatStatus.Available;
        HolderReference = null;
        ReservedAtUtc = null;
        ReservationExpiresAtUtc = null;
    }

    public bool IsReservationExpired(DateTimeOffset now) =>
        Status is SeatStatus.Reserved && ReservationExpiresAtUtc < now;

    public bool IsSelectableBy(string holderReference, DateTimeOffset now) =>
        Status is SeatStatus.Available
        || (Status is SeatStatus.Reserved
            && (IsReservationExpired(now)
                || string.Equals(HolderReference, holderReference, StringComparison.Ordinal)));
}
