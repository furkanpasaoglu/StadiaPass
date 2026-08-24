using System.Globalization;
using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.Tickets.Commands.ConfirmTicketPurchase;

internal sealed class ConfirmTicketPurchaseCommandHandler(
    IMatchRepository matchRepository,
    ITicketRepository ticketRepository,
    IPaymentService paymentService,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider) : IRequestHandler<ConfirmTicketPurchaseCommand, TicketDto>
{
    public async Task<TicketDto> Handle(ConfirmTicketPurchaseCommand request, CancellationToken cancellationToken)
    {
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

        var ticket = Ticket.IssueFor(match, seat, now);

        await ticketRepository.AddAsync(ticket, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ticket.ToDto();
    }

    /// <summary>
    /// The amount comes from the seat rather than from the request: a client that could name its own price
    /// would be a rather serious hole. The reference is stable per seat, which is what lets a provider treat
    /// a repeated call as the same charge.
    /// </summary>
    private static PaymentRequest BuildPaymentRequest(
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
                $"{match.HomeTeam} vs {match.AwayTeam} - seat {seat.SeatNumber}"));

    /// <summary>Card numbers are typed with spaces and dashes; providers want the digits alone.</summary>
    private static string Digits(string cardNumber) =>
        string.Concat(cardNumber.Where(char.IsAsciiDigit));
}
