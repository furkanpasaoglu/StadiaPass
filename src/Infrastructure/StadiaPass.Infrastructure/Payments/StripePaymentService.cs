using Microsoft.Extensions.Logging;
using StadiaPass.Application.Infrastructure.Abstractions;
using Stripe;

namespace StadiaPass.Infrastructure.Payments;

/// <summary>
/// Talks to Stripe's test API for real: a PaymentIntent is created and confirmed in one call, keyed by the
/// seat so a double-click or a retried request is the same charge rather than a second one.
/// </summary>
/// <remarks>
/// The card never leaves this process. Stripe rejects raw card numbers from a server, so the number is
/// resolved to the test payment method token that stands for it - which is also the shape a production
/// integration has, where Stripe.js tokenises the card in the browser and the server only ever handles an id.
/// </remarks>
internal sealed partial class StripePaymentService(
    IStripeClient stripeClient,
    ILogger<StripePaymentService> logger) : IPaymentService
{
    private const string SucceededStatus = "succeeded";

    public async Task<PaymentResult> ProcessPaymentAsync(
        PaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (StripeTestCards.TokenFor(request.Card.CardNumber) is not { } paymentMethodToken)
        {
            // Better a plain answer than a confusing rejection from Stripe about raw card data.
            UnknownTestCard(logger, request.Card.MaskedNumber);

            return PaymentResult.Failure(
                "unsupported_test_card",
                "Stripe test mode only accepts its own test cards. Use 4242 4242 4242 4242, or switch "
                + "PaymentProvider:Type to Mock to accept any card number locally.");
        }

        try
        {
            var intent = await new PaymentIntentService(stripeClient).CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = ToMinorUnits(request.Amount.Amount),
                    Currency = ToStripeCurrency(request.Amount.Currency),
                    PaymentMethod = paymentMethodToken,
                    Description = request.Description,
                    Confirm = true,

                    // What a webhook carries back. An event arriving weeks later says nothing but "this
                    // charge"; without these it cannot be turned into a seat, and a dispute becomes a number
                    // somebody has to look up by hand.
                    Metadata = new Dictionary<string, string>(request.Correlation, StringComparer.Ordinal),

                    // Nobody is watching a browser at this point, so a redirect-based method would strand the
                    // purchase half finished.
                    AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                    {
                        Enabled = true,
                        AllowRedirects = "never"
                    }
                },
                new RequestOptions { IdempotencyKey = request.Reference },
                cancellationToken);

            if (string.Equals(intent.Status, SucceededStatus, StringComparison.Ordinal))
            {
                PaymentAccepted(logger, intent.Id, request.Card.MaskedNumber);

                return PaymentResult.Success(intent.Id);
            }

            PaymentUnfinished(logger, intent.Id, intent.Status);

            return PaymentResult.Failure(
                intent.Status,
                $"Stripe left the payment in state '{intent.Status}' instead of completing it.");
        }
        catch (StripeException exception)
        {
            // A decline arrives as an exception from the SDK, but to the customer it is an ordinary answer,
            // so it comes back as a failed result rather than a 500. The decline code is the specific one -
            // "insufficient_funds" rather than the blanket "card_declined" - because that is what the
            // customer needs to read.
            var code = exception.StripeError?.DeclineCode
                       ?? exception.StripeError?.Code
                       ?? exception.StripeError?.Type
                       ?? "stripe_error";

            PaymentRejected(logger, request.Card.MaskedNumber, code);

            return PaymentResult.Failure(code, exception.StripeError?.Message ?? exception.Message);
        }
    }

    /// <summary>
    /// Refunds against the PaymentIntent rather than a charge id: that is the identifier the rest of the
    /// system already carries, and it stays correct even when a payment settles into more than one charge.
    /// The idempotency key is derived from the transaction, so a retried unwind gives the money back once.
    /// </summary>
    public async Task<PaymentResult> RefundPaymentAsync(
        string transactionId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var refund = await new RefundService(stripeClient).CreateAsync(
                new RefundCreateOptions
                {
                    PaymentIntent = transactionId,
                    Amount = ToMinorUnits(amount)
                },
                new RequestOptions { IdempotencyKey = $"refund:{transactionId}" },
                cancellationToken);

            if (string.Equals(refund.Status, SucceededStatus, StringComparison.Ordinal))
            {
                RefundIssued(logger, refund.Id, transactionId);

                return PaymentResult.Success(refund.Id);
            }

            // Pending is not a failure, but it is not money back either, so the caller is told the truth
            // rather than a comfortable version of it.
            RefundUnfinished(logger, refund.Id, transactionId, refund.Status);

            return PaymentResult.Failure(
                refund.Status,
                $"Stripe left the refund of {transactionId} in state '{refund.Status}'.");
        }
        catch (StripeException exception)
        {
            // No customer is waiting to read this one - their request already failed. It is logged at Error
            // because it means money is sitting with the provider that nothing in the system will claim back.
            var code = exception.StripeError?.Code ?? exception.StripeError?.Type ?? "stripe_error";

            RefundFailed(logger, transactionId, code, exception);

            return PaymentResult.Failure(code, exception.StripeError?.Message ?? exception.Message);
        }
    }

    /// <summary>
    /// Stripe wants the currency in lower case. Built a character at a time on purpose: the string-level
    /// lowercase methods carry a globalization warning that does not apply to a three letter ISO code.
    /// </summary>
    private static string ToStripeCurrency(string currency) =>
        string.Create(currency.Length, currency, static (destination, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                destination[index] = char.ToLowerInvariant(source[index]);
            }
        });

    /// <summary>
    /// Stripe counts in the currency's smallest unit. Both currencies this project deals in have two decimal
    /// places; a zero-decimal currency such as JPY would need its own case here.
    /// </summary>
    private static long ToMinorUnits(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    [LoggerMessage(
        EventId = 6100,
        Level = LogLevel.Information,
        Message = "Stripe confirmed payment intent {PaymentIntentId} for card {MaskedNumber}")]
    private static partial void PaymentAccepted(ILogger logger, string paymentIntentId, string maskedNumber);

    [LoggerMessage(
        EventId = 6101,
        Level = LogLevel.Warning,
        Message = "Stripe left payment intent {PaymentIntentId} in state {Status}")]
    private static partial void PaymentUnfinished(ILogger logger, string paymentIntentId, string status);

    [LoggerMessage(
        EventId = 6102,
        Level = LogLevel.Information,
        Message = "Stripe rejected card {MaskedNumber}: {FailureCode}")]
    private static partial void PaymentRejected(ILogger logger, string maskedNumber, string failureCode);

    [LoggerMessage(
        EventId = 6103,
        Level = LogLevel.Warning,
        Message = "Card {MaskedNumber} is not one of Stripe's test cards, so no charge was attempted")]
    private static partial void UnknownTestCard(ILogger logger, string maskedNumber);

    [LoggerMessage(
        EventId = 6104,
        Level = LogLevel.Information,
        Message = "Stripe refund {RefundId} returned the money taken by payment intent {PaymentIntentId}")]
    private static partial void RefundIssued(ILogger logger, string refundId, string paymentIntentId);

    [LoggerMessage(
        EventId = 6105,
        Level = LogLevel.Warning,
        Message = "Stripe left refund {RefundId} of payment intent {PaymentIntentId} in state {Status}")]
    private static partial void RefundUnfinished(
        ILogger logger,
        string refundId,
        string paymentIntentId,
        string status);

    [LoggerMessage(
        EventId = 6106,
        Level = LogLevel.Error,
        Message = "Stripe refused to refund payment intent {PaymentIntentId} ({FailureCode}); "
            + "that charge is still with the provider and needs to be given back by hand")]
    private static partial void RefundFailed(
        ILogger logger,
        string paymentIntentId,
        string failureCode,
        Exception exception);
}
