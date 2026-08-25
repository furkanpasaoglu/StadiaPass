using System.Globalization;
using Microsoft.Extensions.Logging;
using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Payments.Events;
using StadiaPass.Application.Tickets.Events;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.Tickets.Commands.ConfirmTicketPurchase;

internal sealed partial class ConfirmTicketPurchaseCommandHandler(
    IMatchRepository matchRepository,
    ITicketRepository ticketRepository,
    IPaymentService paymentService,
    IDistributedLock distributedLock,
    IOutbox outbox,
    IRefundLedger refundLedger,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    ILogger<ConfirmTicketPurchaseCommandHandler> logger)
    : IRequestHandler<ConfirmTicketPurchaseCommand, TicketDto>
{
    /// <summary>How long the write after the payment is allowed to take before the lease is given up on.</summary>
    private static readonly TimeSpan WriteAllowance = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Long enough to cover a slow provider and the write that follows, and no longer. This is not the ten
    /// minute reservation window: a hold is a promise to a customer, whereas this lease only has to outlive
    /// one attempt to pay. Set it to the window and a process that dies mid-purchase would make the seat
    /// unbuyable for ten minutes - including to the very person still holding it, whose hold would expire
    /// while they waited.
    /// </summary>
    /// <remarks>
    /// Asked of the provider rather than written down here. A flat minute looked right and was not: the
    /// Stripe adapter's timeout is configuration, and the SDK retries the network underneath it, so the
    /// worst case is that timeout several times over. A lease that runs out while the call it guards is
    /// still running is a lock that quietly stops guarding - the seat opens up mid-payment and two cards
    /// get charged for it, which is the exact thing this lock exists to prevent.
    /// </remarks>
    private TimeSpan SeatLockLease => paymentService.WorstCaseDuration + WriteAllowance;

    public async Task<TicketDto> Handle(ConfirmTicketPurchaseCommand request, CancellationToken cancellationToken)
    {
        // Turned away at the door. The seat's concurrency token is what makes a double sale impossible, but
        // it only says so at the very end - by which point the loser has had their card charged and refunded
        // for a seat they were never going to get. A charge and a refund on somebody's statement for nothing
        // is worth avoiding, and this is what avoids it.
        await using var seatLock =
            await distributedLock.TryAcquireAsync(SeatLockKey(request), SeatLockLease, cancellationToken)
            ?? throw new ConcurrencyConflictException(
                $"Seat {request.SeatNumber} is being bought by somebody else at this very moment. "
                + "Please try again in a few seconds.");

        var match = await matchRepository.GetWithSeatAsync(request.MatchId, request.SeatNumber, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        var now = dateTimeProvider.UtcNow;

        // Every rule of the sale is checked first, without changing anything. Charging a card and only then
        // discovering the hold had expired would leave the customer paid up and seatless.
        var seat = match.EnsureSeatCanBeSoldTo(request.SeatNumber, currentUser.Reference, now);

        var payment = await paymentService.ProcessPaymentAsync(
            BuildPaymentRequest(request, match, seat), cancellationToken);

        if (!payment.IsSuccessful)
        {
            // Nothing has been written yet, so there is nothing to undo: the seat is still Reserved for this
            // caller until the hold runs out and they can try again with another card.
            throw new PaymentFailedException(
                payment.FailureCode ?? "payment_failed",
                payment.FailureMessage ?? "The payment could not be completed.");
        }

        match.ConfirmSeatSale(request.SeatNumber, currentUser.Reference, now);

        var ticket = Ticket.IssueFor(match, seat, payment.TransactionId!, now);

        await ticketRepository.AddAsync(ticket, cancellationToken);

        await WriteTheSaleAsync(request, match, seat, payment, ticket, now, cancellationToken);

        return ticket.ToDto();
    }

    /// <summary>
    /// The counters, the seat and the ticket land together or not at all. Everything in here happens after
    /// the card was charged, so every exit through the failure path gives the money back before it lets the
    /// exception go on.
    /// </summary>
    private async Task WriteTheSaleAsync(
        ConfirmTicketPurchaseCommand request,
        Match match,
        MatchSeat seat,
        PaymentResult payment,
        Ticket ticket,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Staged before the transaction opens and written by the save inside it, so the message and the sale
        // still share one fate - which is the whole point of an outbox. Publishing to a broker only once the
        // sale is safely committed sounds like the careful order, but it leaves a gap: the process can stop
        // in between and the ticket is sold with nobody downstream ever told.
        //
        // Staged out here rather than in the delegate because the retrying execution strategy runs that
        // delegate again after a transient failure, and a rolled-back transaction does not untrack what was
        // added to it. A second pass would stage a second copy, both would be saved by the attempt that
        // succeeded, and the customer would be sent two confirmations for one seat.
        outbox.Enqueue(BuildPurchasedEvent(ticket, match, payment, now));

        // Takes the counters out of the save's hands now and hands back the update that writes them. That
        // update takes the match row, which is the coarsest lock in the system - one row per fixture, wanted
        // by every sale of it - so it is issued last, immediately before the commit, rather than at the top
        // where every other sale of this match would queue behind this one's seat, ticket and outbox writes.
        var writeCounters = matchRepository.PrepareSeatSaleCounters(match);

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    await unitOfWork.SaveChangesAsync(token);

                    await writeCounters(token);
                },
                cancellationToken);
        }
        catch (ConcurrencyConflictException exception)
        {
            // The guards passed on the copy this request read, and somebody else wrote that seat before this
            // transaction reached the database. The xmin no longer matches, so the UPDATE touched no rows and
            // everything rolled back - counters, seat, ticket. Everything except the charge.
            SeatLostTheRace(logger, request.SeatNumber, request.MatchId, payment.TransactionId, exception);

            await GiveTheMoneyBackAsync(match, payment, seat, "the seat was taken by another sale", now);

            throw new ConcurrencyConflictException(
                $"Seat {request.SeatNumber} was reserved or sold by another transaction while this purchase "
                + "was being completed. Please pick another seat.",
                exception);
        }
        catch (Exception exception)
        {
            // Losing the race is only the likeliest way to fail here, not the only one. A dropped connection
            // strands the money just as thoroughly, so it comes back the same way.
            SaleWriteFailed(logger, request.SeatNumber, request.MatchId, payment.TransactionId, exception);

            await GiveTheMoneyBackAsync(match, payment, seat, "the sale could not be written", now);

            throw;
        }
    }

    /// <summary>
    /// Deliberately not handed the request's cancellation token: the customer may well have closed the tab by
    /// now, and that is no reason to keep their money. Nothing in here is allowed to throw - the caller is
    /// already on its way to an error, and replacing that error with this one would hide what actually went
    /// wrong.
    /// </summary>
    /// <remarks>
    /// Two goes, and they fail differently on purpose. The first is the refund itself, because the ordinary
    /// reason to be here is losing a race for a seat, the database is perfectly healthy, and the money is
    /// better back in a second than in five. When that does not work the second is a row: the refund is
    /// written to the outbox, where the sweeper carries it, the broker redelivers it and the dead-message
    /// gauge counts it if it never succeeds. It has to be a row rather than another attempt, because an
    /// attempt that fails leaves nothing behind, and a log line nobody reads is how money gets lost.
    /// </remarks>
    private async Task GiveTheMoneyBackAsync(
        Match match,
        PaymentResult payment,
        MatchSeat seat,
        string reason,
        DateTimeOffset now)
    {
        if (payment.TransactionId is not { Length: > 0 } transactionId)
        {
            return;
        }

        var amount = seat.Price.Amount;

        try
        {
            var refund = await paymentService.RefundPaymentAsync(transactionId, amount, CancellationToken.None);

            if (refund.IsSuccessful)
            {
                RefundIssued(logger, amount, transactionId, refund.TransactionId!);

                return;
            }

            RefundRejected(logger, amount, transactionId, refund.FailureCode ?? "unknown");
        }
        catch (Exception exception)
        {
            RefundCrashed(logger, amount, transactionId, exception);
        }

        // Written down rather than given up on. Whether the provider refused or the call threw, the money is
        // still ours to give back and something other than a logger has to remember that.
        await refundLedger.RecordAsync(
            new RefundOwedEvent(
                transactionId,
                amount,
                seat.Price.Currency,
                match.Id,
                seat.SeatNumber.ToString(),
                reason,
                now),
            CancellationToken.None);
    }

    /// <summary>
    /// The amount comes from the seat rather than from the request: a client that could name its own price
    /// would be a rather serious hole. The reference is stable per seat, which is what lets a provider treat
    /// a repeated call as the same charge.
    /// </summary>
    private PaymentRequest BuildPaymentRequest(
        ConfirmTicketPurchaseCommand request,
        Match match,
        MatchSeat seat) =>
        new(
            seat.Price,
            new PaymentCard(
                request.CardHolderName.Trim(),
                Digits(request.CardNumber),
                request.ExpirationMonth,
                request.ExpirationYear,
                request.Cvv.Trim()),
            string.Create(CultureInfo.InvariantCulture, $"stadiapass:{match.Id}:{seat.SeatNumber}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"{match.HomeTeam} vs {match.AwayTeam} - seat {seat.SeatNumber}"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["matchId"] = match.Id.ToString(),
                ["seatNumber"] = seat.SeatNumber.ToString(),
                ["holderReference"] = currentUser.Reference
            });


    /// <summary>
    /// Everything a consumer could want about the purchase, so nothing downstream has to come back to the
    /// database for it. That is what makes this message safe to put on a queue later.
    /// </summary>
    private TicketPurchasedEvent BuildPurchasedEvent(
        Ticket ticket,
        Match match,
        PaymentResult payment,
        DateTimeOffset now) =>
        new(
            ticket.Id,
            ticket.AccessCode,
            match.Id,
            match.HomeTeam,
            match.AwayTeam,
            match.VenueName,
            match.KickOffUtc,
            ticket.SeatNumber.ToString(),
            ticket.Price.Amount,
            ticket.Price.Currency,
            ticket.HolderReference,
            currentUser.Email,
            payment.TransactionId!,
            now);

    /// <summary>Card numbers are typed with spaces and dashes; providers want the digits alone.</summary>
    private static string Digits(string cardNumber) =>
        string.Concat(cardNumber.Where(char.IsAsciiDigit));

    /// <summary>
    /// The seat's own id would be the obvious key, and getting it would mean the database round trip this
    /// lock exists to avoid. The match and the seat number identify a seat just as exactly - there is a
    /// unique index on precisely that pair - and both arrive with the request.
    /// </summary>
    /// <remarks>
    /// Parsed rather than used as typed, because <c>maraton-01-7</c> and <c>MARATON-1-7</c> are the same seat
    /// and would otherwise be two different keys, which is a lock that quietly guards nothing.
    /// </remarks>
    private static string SeatLockKey(ConfirmTicketPurchaseCommand request) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"lock:seat:{request.MatchId}:{SeatNumber.Parse(request.SeatNumber)}");

    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Warning,
        Message = "Seat {SeatNumber} of match {MatchId} was taken by another transaction after payment "
            + "{PaymentTransactionId} was captured; the purchase was rolled back")]
    private static partial void SeatLostTheRace(
        ILogger logger,
        string seatNumber,
        Guid matchId,
        string? paymentTransactionId,
        Exception exception);

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Error,
        Message = "Writing the sale of seat {SeatNumber} of match {MatchId} failed after payment "
            + "{PaymentTransactionId} was captured")]
    private static partial void SaleWriteFailed(
        ILogger logger,
        string seatNumber,
        Guid matchId,
        string? paymentTransactionId,
        Exception exception);

    [LoggerMessage(
        EventId = 3102,
        Level = LogLevel.Information,
        Message = "Refunded {Amount} of payment {PaymentTransactionId} as refund {RefundTransactionId}")]
    private static partial void RefundIssued(
        ILogger logger,
        decimal amount,
        string paymentTransactionId,
        string refundTransactionId);

    [LoggerMessage(
        EventId = 3103,
        Level = LogLevel.Error,
        Message = "The provider refused to refund {Amount} of payment {PaymentTransactionId} ({FailureCode}); "
            + "that money is still held against a sale that never happened and needs giving back by hand")]
    private static partial void RefundRejected(
        ILogger logger,
        decimal amount,
        string paymentTransactionId,
        string failureCode);

    [LoggerMessage(
        EventId = 3104,
        Level = LogLevel.Error,
        Message = "Refunding {Amount} of payment {PaymentTransactionId} threw; that money is still held "
            + "against a sale that never happened and needs giving back by hand")]
    private static partial void RefundCrashed(
        ILogger logger,
        decimal amount,
        string paymentTransactionId,
        Exception exception);
}
