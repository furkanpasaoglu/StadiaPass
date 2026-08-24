using NSubstitute;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Tickets.Commands.ConfirmTicketPurchase;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.UnitTests.Tickets;

/// <summary>
/// Money makes the order of operations matter. The card is charged only once the sale is known to be
/// possible, and a decline must leave the seat exactly as it was - still held for the caller, nothing
/// written, nothing to unwind.
/// </summary>
public sealed class ConfirmTicketPurchaseCommandHandlerTests
{
    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly ITicketRepository _ticketRepository = Substitute.For<ITicketRepository>();

    private readonly IPaymentService _paymentService = Substitute.For<IPaymentService>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    private readonly ConfirmTicketPurchaseCommandHandler _handler;

    public ConfirmTicketPurchaseCommandHandlerTests()
    {
        _currentUser.Reference.Returns(TestData.CurrentUserId);
        _currentUser.IsAuthenticated.Returns(true);
        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        _handler = new ConfirmTicketPurchaseCommandHandler(
            _matchRepository, _ticketRepository, _paymentService, _unitOfWork, _currentUser, _dateTimeProvider);
    }

    [Fact]
    public async Task Should_ChargeTheCardAndIssueTheTicket_When_TheSeatIsHeldByTheCaller()
    {
        // Arrange
        var match = GivenASeatHeldBy(TestData.CurrentUserId);
        GivenThePaymentSucceeds();

        // Act
        var ticket = await _handler.Handle(Command(), CancellationToken.None);

        // Assert
        ticket.SeatNumber.Should().Be(TestData.SeatNumber);
        TestData.SeatOf(match, TestData.SeatNumber).Status.Should().Be(SeatStatus.Sold);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ChargeThePriceOfTheSeat_When_ThePurchaseIsConfirmed()
    {
        // Arrange
        var match = GivenASeatHeldBy(TestData.CurrentUserId);
        GivenThePaymentSucceeds();

        // Act
        await _handler.Handle(Command(), CancellationToken.None);

        // Assert - the amount comes from the seat, never from anything the caller sent.
        var seat = TestData.SeatOf(match, TestData.SeatNumber);
        await _paymentService
            .Received(1)
            .ProcessPaymentAsync(
                Arg.Is<PaymentRequest>(request => request.Amount.Amount == seat.Price.Amount),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_LeaveTheSeatReservedAndSaveNothing_When_ThePaymentIsDeclined()
    {
        // Arrange
        var match = GivenASeatHeldBy(TestData.CurrentUserId);
        _paymentService
            .ProcessPaymentAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymentResult.Failure("insufficient_funds", "The card has insufficient funds."));

        // Act
        var buying = () => _handler.Handle(Command(), CancellationToken.None);

        // Assert - the hold survives, so the customer can try another card before it runs out.
        await buying.Should().ThrowAsync<PaymentFailedException>();
        TestData.SeatOf(match, TestData.SeatNumber).Status.Should().Be(SeatStatus.Reserved);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _ticketRepository.DidNotReceive().AddAsync(Arg.Any<Domain.Tickets.Ticket>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotTouchTheCard_When_TheSeatIsHeldBySomebodyElse()
    {
        // Arrange
        GivenASeatHeldBy(TestData.OtherUserId);
        GivenThePaymentSucceeds();

        // Act
        var buying = () => _handler.Handle(Command(), CancellationToken.None);

        // Assert - charging first and discovering the seat is not ours afterwards would take money for
        // nothing, so the rules are checked before the provider is called at all.
        await buying.Should().ThrowAsync<Domain.Common.DomainException>();
        await _paymentService
            .DidNotReceive()
            .ProcessPaymentAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotTouchTheCard_When_TheHoldHasAlreadyExpired()
    {
        // Arrange
        GivenASeatHeldBy(TestData.CurrentUserId);
        _dateTimeProvider.UtcNow.Returns(TestData.Now + Match.ReservationWindow + TimeSpan.FromSeconds(1));
        GivenThePaymentSucceeds();

        // Act
        var buying = () => _handler.Handle(Command(), CancellationToken.None);

        // Assert
        await buying.Should().ThrowAsync<Domain.Common.DomainException>();
        await _paymentService
            .DidNotReceive()
            .ProcessPaymentAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>());
    }

    private static ConfirmTicketPurchaseCommand Command() =>
        new(
            Guid.CreateVersion7(),
            TestData.SeatNumber,
            CardHolderName: "FURKAN PASAOGLU",
            CardNumber: "4242 4242 4242 4242",
            ExpirationMonth: 12,
            ExpirationYear: TestData.Now.Year + 2,
            Cvv: "123");

    private Match GivenASeatHeldBy(string holderReference)
    {
        var match = TestData.FootballMatch();

        match.ReserveSeat(TestData.SeatNumber, holderReference, TestData.Now);

        _matchRepository
            .GetWithSeatAsync(Arg.Any<Guid>(), TestData.SeatNumber, Arg.Any<CancellationToken>())
            .Returns(match);

        return match;
    }

    private void GivenThePaymentSucceeds() =>
        _paymentService
            .ProcessPaymentAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymentResult.Success("mock_reference"));
}
