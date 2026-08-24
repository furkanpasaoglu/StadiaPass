using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Tickets.Commands.ConfirmTicketPurchase;
using StadiaPass.Application.Tickets.Events;
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
    private const string PaymentTransactionId = "pi_test_reference";

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly ITicketRepository _ticketRepository = Substitute.For<ITicketRepository>();

    private readonly IPaymentService _paymentService = Substitute.For<IPaymentService>();

    private readonly IDistributedLock _distributedLock = Substitute.For<IDistributedLock>();

    private readonly IDistributedLockHandle _seatLock = Substitute.For<IDistributedLockHandle>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly IPublisher _publisher = Substitute.For<IPublisher>();

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    private readonly ConfirmTicketPurchaseCommandHandler _handler;

    public ConfirmTicketPurchaseCommandHandlerTests()
    {
        _currentUser.Reference.Returns(TestData.CurrentUserId);
        _currentUser.IsAuthenticated.Returns(true);
        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        // A substituted transaction that never runs its body would let every assertion below pass against a
        // handler that saves nothing at all, so the fake actually executes what it is given.
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task>>()(call.Arg<CancellationToken>()));

        // Nobody else is buying this seat unless a test says so; a substitute left to itself would answer
        // null, which is the handler's signal that the seat is taken.
        _distributedLock
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_seatLock);

        _handler = new ConfirmTicketPurchaseCommandHandler(
            _matchRepository,
            _ticketRepository,
            _paymentService,
            _distributedLock,
            _unitOfWork,
            _publisher,
            _currentUser,
            _dateTimeProvider,
            NullLogger<ConfirmTicketPurchaseCommandHandler>.Instance);
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

    [Fact]
    public async Task Should_TurnTheRequestAwayAtTheDoor_When_TheSeatIsAlreadyBeingBought()
    {
        // Arrange
        GivenASeatHeldBy(TestData.CurrentUserId);
        GivenThePaymentSucceeds();
        _distributedLock
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((IDistributedLockHandle?)null);

        // Act
        var buying = () => _handler.Handle(Command(), CancellationToken.None);

        // Assert - the point of the lock is the work that does not happen: no seat map read, no charge, and
        // therefore no refund to make afterwards.
        await buying.Should().ThrowAsync<ConcurrencyConflictException>();
        await _matchRepository
            .DidNotReceive()
            .GetWithSeatAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _paymentService
            .DidNotReceive()
            .ProcessPaymentAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_LockTheSeatByItsCanonicalNumber_When_ThePurchaseIsAttempted()
    {
        // Arrange - the same seat written the way a careless caller might write it.
        var command = Command() with { SeatNumber = TestData.SeatNumber.ToLowerInvariant() };
        GivenASeatHeldBy(TestData.CurrentUserId);
        GivenThePaymentSucceeds();

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert - two spellings of one seat have to reach the same key, or the lock guards nothing.
        await _distributedLock
            .Received(1)
            .TryAcquireAsync(
                $"lock:seat:{command.MatchId}:{TestData.SeatNumber}",
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReleaseTheSeatLock_When_ThePurchaseFails()
    {
        // Arrange
        GivenASeatHeldBy(TestData.CurrentUserId);
        _paymentService
            .ProcessPaymentAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymentResult.Failure("insufficient_funds", "The card has insufficient funds."));

        // Act
        var buying = () => _handler.Handle(Command(), CancellationToken.None);

        // Assert - a declined card must not leave the seat locked until the lease runs out; the customer is
        // expected to come straight back with another one.
        await buying.Should().ThrowAsync<PaymentFailedException>();
        await _seatLock.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Should_RefuseTheSaleNamingTheSeat_When_AnotherTransactionWonTheRaceToTheRow()
    {
        // Arrange - the guards all passed on the copy this request read, and the row still changed underneath
        // it before the write landed. That is exactly the window the seat's concurrency token closes.
        GivenASeatHeldBy(TestData.CurrentUserId);
        GivenThePaymentSucceeds();
        GivenTheWriteLosesTheRace();

        // Act
        var buying = () => _handler.Handle(Command(), CancellationToken.None);

        // Assert - the caller is told which seat went, not that a row version failed to match.
        var thrown = await buying.Should().ThrowAsync<ConcurrencyConflictException>();
        thrown.Which.Message.Should().Contain(TestData.SeatNumber);
        thrown.Which.InnerException.Should().BeOfType<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Should_GiveTheMoneyBack_When_TheSaleIsLostAfterTheCardWasCharged()
    {
        // Arrange
        var match = GivenASeatHeldBy(TestData.CurrentUserId);
        GivenThePaymentSucceeds();
        GivenTheWriteLosesTheRace();

        // Act
        var buying = () => _handler.Handle(Command(), CancellationToken.None);

        // Assert - the customer paid for a seat they did not get, so the charge is reversed for exactly what
        // the seat cost before the failure is allowed to travel any further.
        await buying.Should().ThrowAsync<ConcurrencyConflictException>();
        await _paymentService
            .Received(1)
            .RefundPaymentAsync(
                PaymentTransactionId,
                TestData.SeatOf(match, TestData.SeatNumber).Price.Amount,
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotAnnounceThePurchase_When_TheSaleWasNeverWritten()
    {
        // Arrange
        GivenASeatHeldBy(TestData.CurrentUserId);
        GivenThePaymentSucceeds();
        GivenTheWriteLosesTheRace();

        // Act
        var buying = () => _handler.Handle(Command(), CancellationToken.None);

        // Assert - mailing somebody a ticket for a sale that rolled back is worse than not mailing at all.
        await buying.Should().ThrowAsync<ConcurrencyConflictException>();
        await _publisher
            .DidNotReceive()
            .Publish(Arg.Any<TicketPurchasedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_AnnounceThePurchaseWithEverythingAConsumerNeeds_When_TheSaleIsCommitted()
    {
        // Arrange
        var match = GivenASeatHeldBy(TestData.CurrentUserId);
        GivenThePaymentSucceeds();

        // Act
        var ticket = await _handler.Handle(Command(), CancellationToken.None);

        // Assert - the message has to stand on its own, because the consumer of it will one day be on the
        // other side of a queue with no database of ours to ask.
        await _publisher
            .Received(1)
            .Publish(
                Arg.Is<TicketPurchasedEvent>(published =>
                    published.TicketId == ticket.Id
                    && published.AccessCode == ticket.AccessCode
                    && published.MatchId == match.Id
                    && published.HomeTeam == match.HomeTeam
                    && published.AwayTeam == match.AwayTeam
                    && published.VenueName == match.VenueName
                    && published.SeatNumber == TestData.SeatNumber
                    && published.HolderReference == TestData.CurrentUserId
                    && published.PaymentTransactionId == PaymentTransactionId),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_LeaveTheMatchCountersToTheDatabase_When_TheSaleIsWritten()
    {
        // Arrange
        var match = GivenASeatHeldBy(TestData.CurrentUserId);
        GivenThePaymentSucceeds();

        // Act
        await _handler.Handle(Command(), CancellationToken.None);

        // Assert - the totals this request read are never what gets written; the database works them out for
        // itself, inside the same transaction as the seat.
        await _matchRepository.Received(1).ApplySeatSaleToCountersAsync(match, Arg.Any<CancellationToken>());
        await _unitOfWork
            .Received(1)
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
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

        // Any spelling of the seat: the real repository parses the number before it looks it up, so a
        // substitute matching the exact string would be stricter than the thing it stands in for.
        _matchRepository
            .GetWithSeatAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(match);

        return match;
    }

    private void GivenThePaymentSucceeds()
    {
        _paymentService
            .ProcessPaymentAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymentResult.Success(PaymentTransactionId));

        _paymentService
            .RefundPaymentAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(PaymentResult.Success("mock_refund"));
    }

    /// <summary>Somebody else wrote the seat row between this request reading it and saving it.</summary>
    private void GivenTheWriteLosesTheRace() =>
        _unitOfWork
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("The MatchSeat was changed by another transaction."));
}
