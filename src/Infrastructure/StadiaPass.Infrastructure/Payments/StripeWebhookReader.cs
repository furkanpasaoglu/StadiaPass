using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Payments.Events;
using Stripe;

namespace StadiaPass.Infrastructure.Payments;

/// <summary>
/// Checks that a webhook really came from Stripe, and turns the ones worth acting on into this system's own
/// events.
/// </summary>
/// <remarks>
/// The signature is not a formality. The endpoint it guards is anonymous and public, so without this anybody
/// who knows the URL can post <c>payment_intent.succeeded</c> and be given a ticket. <see cref="EventUtility"/>
/// recomputes the HMAC over the raw body with the signing secret and refuses anything that does not match,
/// including a replay of a genuine event from more than five minutes ago.
/// </remarks>
internal sealed partial class StripeWebhookReader(
    IOptions<PaymentOptions> options,
    ILogger<StripeWebhookReader> logger) : IPaymentWebhookReader
{
    private readonly PaymentOptions _options = options.Value;

    public bool TryRead(
        string payload,
        string? signature,
        [NotNullWhen(true)] out PaymentWebhookEvent? webhookEvent)
    {
        webhookEvent = null;

        if (_options.WebhookSecret is not { Length: > 0 } secret)
        {
            // Refused rather than waved through. An unverifiable webhook is an anonymous stranger claiming a
            // payment succeeded, and accepting one because the secret is missing is the whole vulnerability.
            NoSecretConfigured(logger);

            return false;
        }

        if (signature is not { Length: > 0 })
        {
            // Guarded here rather than left to the parser, which answers a missing header with a
            // NullReferenceException - a 500 and a stack trace, on a public endpoint, for a request that is
            // simply not signed.
            SignatureRejected(logger, "no signature header was sent");

            return false;
        }

        Event stripeEvent;

        try
        {
            // throwOnApiVersionMismatch is deliberately off. A Stripe account has its own API version, set
            // in the dashboard and quite reasonably not the one this SDK was built against, and refusing
            // every event over that would turn a working integration into a silent outage. The four fields
            // read below - id, metadata, amount, currency - have been stable across versions for years.
            stripeEvent = EventUtility.ConstructEvent(
                payload, signature, secret, throwOnApiVersionMismatch: false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Everything, not just StripeException. This is an anonymous endpoint reachable by anyone who
            // knows the URL, and a parser fed deliberately malformed input must not be able to answer with
            // a 500 and a stack trace. Anything that is not a verified event is refused the same way.
            SignatureRejected(logger, exception.Message);

            return false;
        }

        webhookEvent = new PaymentWebhookEvent(stripeEvent.Id, stripeEvent.Type, Translate(stripeEvent));

        return true;
    }

    /// <summary>
    /// Stripe sends a great many event types and this system has a use for three. The rest are verified,
    /// acknowledged and dropped: recording them would fill a table with rows nothing ever reads.
    /// </summary>
    private object? Translate(Event stripeEvent) => stripeEvent.Type switch
    {
        EventTypes.PaymentIntentSucceeded when stripeEvent.Data.Object is PaymentIntent intent =>
            new PaymentSucceeded(
                intent.Id,
                ReadMatchId(intent.Metadata),
                Read(intent.Metadata, "seatNumber"),
                Read(intent.Metadata, "holderReference"),
                ToMajorUnits(intent.Amount),
                intent.Currency.ToUpperInvariant(),
                stripeEvent.Created),

        EventTypes.ChargeDisputeCreated when stripeEvent.Data.Object is Dispute dispute =>
            new PaymentDisputed(
                dispute.PaymentIntentId,
                dispute.Id,
                dispute.Reason,
                ToMajorUnits(dispute.Amount),
                dispute.Currency.ToUpperInvariant(),
                stripeEvent.Created),

        EventTypes.ChargeRefunded when stripeEvent.Data.Object is Charge charge =>
            new PaymentRefunded(
                charge.PaymentIntentId,
                ToMajorUnits(charge.AmountRefunded),
                charge.Currency.ToUpperInvariant(),
                stripeEvent.Created),

        _ => Ignored(stripeEvent.Type)
    };

    private object? Ignored(string eventType)
    {
        EventIgnored(logger, eventType);

        return null;
    }

    /// <summary>
    /// Read defensively. Metadata is set by this application, but an event can outlive the code that wrote
    /// it and a charge created by hand in the dashboard carries none at all.
    /// </summary>
    private static string? Read(IDictionary<string, string>? metadata, string key) =>
        metadata is not null && metadata.TryGetValue(key, out var value) && value is { Length: > 0 }
            ? value
            : null;

    private static Guid? ReadMatchId(IDictionary<string, string>? metadata) =>
        Guid.TryParse(Read(metadata, "matchId"), out var matchId) ? matchId : null;

    /// <summary>Stripe counts in the currency's smallest unit; both currencies here have two decimals.</summary>
    private static decimal ToMajorUnits(long minorUnits) => minorUnits / 100m;

    [LoggerMessage(
        EventId = 6500,
        Level = LogLevel.Error,
        Message = "A webhook arrived but PaymentProvider:WebhookSecret is not set, so nothing can be "
            + "verified and nothing will be accepted")]
    private static partial void NoSecretConfigured(ILogger logger);

    [LoggerMessage(
        EventId = 6501,
        Level = LogLevel.Warning,
        Message = "A webhook was refused because its signature did not hold: {Reason}")]
    private static partial void SignatureRejected(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 6502,
        Level = LogLevel.Debug,
        Message = "Verified webhook of type {ProviderEventType}, which this system has no use for")]
    private static partial void EventIgnored(ILogger logger, string providerEventType);
}
