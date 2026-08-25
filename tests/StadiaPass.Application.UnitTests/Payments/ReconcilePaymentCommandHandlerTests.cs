using Microsoft.Extensions.Logging;
using NSubstitute;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Payments.Commands.ReconcilePayment;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.UnitTests.Payments;

/// <summary>
/// A payment the provider says went through, checked against the tickets this system issued. The interesting
/// case is not the alarm - it is the alarm that must not fire, because the webhook races the checkout that
/// caused it and crying wolf on every busy minute would make the real one worthless.
/// </summary>
public sealed class ReconcilePaymentCommandHandlerTests
{
    private const string PaymentIntentId = "pi_3RxKvQ2eZvKYlo2C0abcdefg";

    private readonly ITicketRepository _ticketRepository = Substitute.For<ITicketRepository>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    private readonly RecordingLogger<ReconcilePaymentCommandHandler> _logger = new();

    private readonly ReconcilePaymentCommandHandler _handler;

    public ReconcilePaymentCommandHandlerTests()
    {
        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        _handler = new ReconcilePaymentCommandHandler(_ticketRepository, _dateTimeProvider, _logger);
    }

    [Fact]
    public async Task Should_SayNothingAlarming_When_ThePaymentAlreadyHasATicket()
    {
        // Arrange - the ordinary case: the checkout did its job and this event has nothing to add.
        GivenATicketExists();

        // Act
        await _handler.Handle(Command(paidSecondsAgo: 3600), CancellationToken.None);

        // Assert
        _logger.Logged(LogLevel.Error).Should().BeFalse();
    }

    [Fact]
    public async Task Should_SayNothingAlarming_When_ThePaymentIsStillYoung()
    {
        // Arrange - Stripe sends payment_intent.succeeded at the same moment it answers the synchronous
        // call, so this can arrive while the checkout that caused it is still writing its sale.
        GivenNoTicketExists();

        // Act
        await _handler.Handle(Command(paidSecondsAgo: 5), CancellationToken.None);

        // Assert - an alarm here would fire on every busy minute of every match, and an alarm nobody trusts
        // is worse than no alarm at all.
        _logger.Logged(LogLevel.Error).Should().BeFalse();
    }

    [Fact]
    public async Task Should_Alarm_When_APaymentOldEnoughToHaveSettledHasNoTicket()
    {
        // Arrange - long past any write this system could still be finishing.
        GivenNoTicketExists();

        // Act
        await _handler.Handle(Command(paidSecondsAgo: 600), CancellationToken.None);

        // Assert - somebody has paid for a seat this system does not think it sold, and only a person can
        // decide whether they get the seat or the money.
        _logger.Logged(LogLevel.Error).Should().BeTrue();
        _logger.Entries.Should().Contain(entry => entry.Message.Contains(PaymentIntentId, StringComparison.Ordinal));
    }

    private static ReconcilePaymentCommand Command(int paidSecondsAgo) =>
        new(
            PaymentIntentId,
            Guid.CreateVersion7(),
            TestData.SeatNumber,
            TestData.CurrentUserId,
            100m,
            "TRY",
            TestData.Now.AddSeconds(-paidSecondsAgo));

    private void GivenNoTicketExists() =>
        _ticketRepository
            .GetByPaymentIntentAsync(PaymentIntentId, Arg.Any<CancellationToken>())
            .Returns((Ticket?)null);

    private void GivenATicketExists()
    {
        var match = TestData.FootballMatch();
        match.ReserveSeat(TestData.SeatNumber, TestData.CurrentUserId, TestData.Now);
        var seat = match.ConfirmSeatSale(TestData.SeatNumber, TestData.CurrentUserId, TestData.Now);

        _ticketRepository
            .GetByPaymentIntentAsync(PaymentIntentId, Arg.Any<CancellationToken>())
            .Returns(Ticket.IssueFor(match, seat, PaymentIntentId, TestData.Now));
    }
}
