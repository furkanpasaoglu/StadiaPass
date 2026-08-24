using System.Security.Cryptography;
using StadiaPass.Domain.Common;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets.Events;

namespace StadiaPass.Domain.Tickets;

/// <summary>
/// The artefact of a completed purchase. A ticket is never conjured out of thin air: it can only be issued
/// for a <see cref="MatchSeat"/> that the match itself has already moved to <see cref="SeatStatus.Sold"/>.
/// </summary>
public sealed class Ticket : AggregateRoot
{
    private Ticket()
    {
    }

    private Ticket(
        Guid id,
        Guid matchId,
        Guid matchSeatId,
        SeatNumber seatNumber,
        Money price,
        string holderReference,
        string accessCode,
        string paymentIntentId,
        DateTimeOffset issuedAtUtc)
        : base(id)
    {
        MatchId = matchId;
        MatchSeatId = matchSeatId;
        SeatNumber = seatNumber;
        Price = price;
        HolderReference = holderReference;
        AccessCode = accessCode;
        PaymentIntentId = paymentIntentId;
        IssuedAtUtc = issuedAtUtc;
        Status = TicketStatus.Issued;
    }

    public Guid MatchId { get; private set; }

    public Guid MatchSeatId { get; private set; }

    public SeatNumber SeatNumber { get; private set; } = null!;

    public Money Price { get; private set; } = null!;

    public string HolderReference { get; private set; } = null!;

    /// <summary>Value printed on the ticket and scanned at the turnstile.</summary>
    public string AccessCode { get; private set; } = null!;

    /// <summary>
    /// The provider's identifier for the charge that paid for this seat. Kept because a payment can come
    /// back to us long after the request that made it - a chargeback, a refund issued from the provider's
    /// own dashboard, or an acknowledgement whose response we never saw - and without it there is no way to
    /// answer the only question that matters then: which ticket is this about?
    /// </summary>
    public string PaymentIntentId { get; private set; } = null!;

    public DateTimeOffset IssuedAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public TicketStatus Status { get; private set; }

    public static Ticket IssueFor(Match match, MatchSeat seat, string paymentIntentId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            throw new DomainRuleViolationException(
                "Ticket.PaymentRequired",
                "A ticket carries the charge that paid for it; there is no such thing as one without.");
        }

        if (seat.Status is not SeatStatus.Sold)
        {
            throw new DomainRuleViolationException(
                "Ticket.SeatNotSold",
                $"A ticket can only be issued for a sold seat; {seat.SeatNumber} is {seat.Status}.");
        }

        if (string.IsNullOrWhiteSpace(seat.HolderReference))
        {
            throw new DomainRuleViolationException(
                "Ticket.HolderRequired", "The seat carries no holder reference.");
        }

        var ticket = new Ticket(
            Guid.CreateVersion7(),
            match.Id,
            seat.Id,
            seat.SeatNumber,
            Money.Create(seat.Price.Amount, seat.Price.Currency),
            seat.HolderReference,
            BuildAccessCode(),
            paymentIntentId,
            now);

        ticket.Raise(new TicketIssuedDomainEvent(
            ticket.Id, match.Id, seat.SeatNumber.ToString(), seat.Price.Amount, seat.HolderReference));

        return ticket;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is TicketStatus.Cancelled)
        {
            throw new DomainRuleViolationException(
                "Ticket.AlreadyCancelled", $"Ticket {AccessCode} is already cancelled.");
        }

        Status = TicketStatus.Cancelled;
        CancelledAtUtc = now;

        Raise(new TicketCancelledDomainEvent(Id, MatchId, MatchSeatId, now));
    }

    /// <summary>
    /// Alphabet without the characters a person misreads at a turnstile: I and 1, O and 0. Twelve of them
    /// carry sixty bits of entropy, drawn from a cryptographic source - a code derived from the clock or a
    /// counter would let anyone who knows roughly when a ticket was bought walk in on it.
    /// </summary>
    private const string AccessCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const int AccessCodeLength = 12;

    private static string BuildAccessCode() =>
        RandomNumberGenerator.GetString(AccessCodeAlphabet, AccessCodeLength);
}
