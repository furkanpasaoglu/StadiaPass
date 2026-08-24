using Microsoft.Extensions.Logging;
using StadiaPass.Application.Infrastructure.Abstractions;

namespace StadiaPass.Infrastructure.Payments;

/// <summary>
/// A payment provider that never leaves the process. It exists so the whole checkout - including the decline
/// path, which is the one that usually goes untested - can be exercised on a laptop with no Stripe account,
/// no key and no network. The outcome is chosen by the card number, following Stripe's own test numbers so
/// switching providers does not mean relearning the fixtures.
/// </summary>
internal sealed partial class MockPaymentService(ILogger<MockPaymentService> logger) : IPaymentService
{
    private const string SuccessPrefix = "4242";

    private const string InsufficientFundsPrefix = "4000";

    public Task<PaymentResult> ProcessPaymentAsync(
        PaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = request.Card.CardNumber switch
        {
            var number when number.StartsWith(SuccessPrefix, StringComparison.Ordinal) =>
                PaymentResult.Success($"mock_{Guid.CreateVersion7():N}"),

            var number when number.StartsWith(InsufficientFundsPrefix, StringComparison.Ordinal) =>
                PaymentResult.Failure("insufficient_funds", "The card has insufficient funds."),

            _ => PaymentResult.Failure("card_declined", "The card was declined.")
        };

        // The card itself is never logged: PaymentCard renders as its masked number wherever it is written.
        if (result.IsSuccessful)
        {
            PaymentAccepted(logger, request.Amount.Amount, request.Amount.Currency, request.Card.MaskedNumber);
        }
        else
        {
            PaymentDeclined(logger, request.Card.MaskedNumber, result.FailureCode!);
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// There is no money to give back, so this only leaves a trace. It still answers like the real thing:
    /// a refund path that is never exercised locally is a refund path nobody finds out is broken.
    /// </summary>
    public Task<PaymentResult> RefundPaymentAsync(
        string transactionId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RefundIssued(logger, amount, transactionId);

        return Task.FromResult(PaymentResult.Success($"mock_refund_{Guid.CreateVersion7():N}"));
    }

    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Information,
        Message = "Mock provider accepted {Amount} {Currency} from card {MaskedNumber}")]
    private static partial void PaymentAccepted(
        ILogger logger,
        decimal amount,
        string currency,
        string maskedNumber);

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "Mock provider declined card {MaskedNumber}: {FailureCode}")]
    private static partial void PaymentDeclined(ILogger logger, string maskedNumber, string failureCode);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Information,
        Message = "Mock provider refunded {Amount} against transaction {TransactionId}")]
    private static partial void RefundIssued(ILogger logger, decimal amount, string transactionId);
}
