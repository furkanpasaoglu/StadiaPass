using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Payments.Commands.IssueOwedRefund;

namespace StadiaPass.Application.UnitTests.Payments;

/// <summary>
/// The second attempt at giving money back: the first one already failed inside the checkout, which is why
/// this runs on the broker at all. The only thing it must never do is finish quietly when the money did not
/// move.
/// </summary>
public sealed class IssueOwedRefundCommandHandlerTests
{
    private const string PaymentIntentId = "pi_3RxKvQ2eZvKYlo2C0abcdefg";

    private readonly IPaymentService _paymentService = Substitute.For<IPaymentService>();

    private readonly IssueOwedRefundCommandHandler _handler;

    public IssueOwedRefundCommandHandlerTests() =>
        _handler = new IssueOwedRefundCommandHandler(
            _paymentService,
            NullLogger<IssueOwedRefundCommandHandler>.Instance);

    [Fact]
    public async Task Should_AskTheProviderForTheAmountItOwes()
    {
        // Arrange
        _paymentService
            .RefundPaymentAsync(PaymentIntentId, 1200m, Arg.Any<CancellationToken>())
            .Returns(PaymentResult.Success("re_1RxKvQ2eZvKYlo2C0abcdefg"));

        // Act
        await _handler.Handle(ACommand(1200m), CancellationToken.None);

        // Assert - a refund for the wrong charge or the wrong amount is a second accounting error on top of
        // the first, and the provider will happily accept both.
        await _paymentService.Received(1).RefundPaymentAsync(PaymentIntentId, 1200m, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_FinishQuietly_When_TheProviderGivesTheMoneyBack()
    {
        // Arrange
        _paymentService
            .RefundPaymentAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(PaymentResult.Success("re_1RxKvQ2eZvKYlo2C0abcdefg"));

        // Act
        var refunding = async () => await _handler.Handle(ACommand(1200m), CancellationToken.None);

        // Assert - throwing on success would send a refund that already happened back round the retry policy.
        await refunding.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Should_Throw_When_TheProviderWillNotRefund()
    {
        // Arrange
        _paymentService
            .RefundPaymentAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(PaymentResult.Failure("charge_already_refunded", "already refunded"));

        // Act
        var refunding = async () => await _handler.Handle(ACommand(1200m), CancellationToken.None);

        // Assert - swallowing here puts the money exactly where this whole mechanism exists to stop it going:
        // nowhere, quietly. Throwing is what hands it to the broker, which redelivers and eventually parks it
        // where somebody can see it.
        var thrown = await refunding.Should().ThrowAsync<PaymentFailedException>();
        thrown.Which.Code.Should().Be("charge_already_refunded");
    }

    private static IssueOwedRefundCommand ACommand(decimal amount) =>
        new(PaymentIntentId, amount, "TRY", "the sale never committed");
}
