using System.Globalization;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Infrastructure.Abstractions;
using Stripe;

namespace StadiaPass.Infrastructure.Payments;

/// <summary>
/// Talks to Stripe's test API for real: a PaymentMethod is created from the card, then a PaymentIntent is
/// created and confirmed in one call. The intent is keyed by the seat, so Stripe treats a double-click or a
/// retried request as the same charge rather than a second one.
/// </summary>
/// <remarks>
/// Sending raw card details from a server is accepted in test mode but is not how a production integration
/// is built: there, Stripe.js or Elements tokenises the card in the browser and the server only ever sees a
/// payment method id, which keeps the card out of this application and out of PCI scope entirely.
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
        try
        {
            var paymentMethod = await new PaymentMethodService(stripeClient).CreateAsync(
                new PaymentMethodCreateOptions
                {
                    Type = "card",
                    Card = new PaymentMethodCardOptions
                    {
                        Number = request.Card.CardNumber,
                        ExpMonth = request.Card.ExpirationMonth,
                        ExpYear = request.Card.ExpirationYear,
                        Cvc = request.Card.Cvv
                    },
                    BillingDetails = new PaymentMethodBillingDetailsOptions
                    {
                        Name = request.Card.CardHolderName
                    }
                },
                cancellationToken: cancellationToken);

            var intent = await new PaymentIntentService(stripeClient).CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = ToMinorUnits(request.Amount.Amount),
                    Currency = ToStripeCurrency(request.Amount.Currency),
                    PaymentMethod = paymentMethod.Id,
                    Description = request.Description,
                    Confirm = true,

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
            // A decline arrives as an exception from the SDK, but it is an ordinary answer to the customer:
            // it comes back as a failed result rather than a 500.
            var code = exception.StripeError?.Code ?? exception.StripeError?.Type ?? "stripe_error";

            PaymentRejected(logger, request.Card.MaskedNumber, code);

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
}
