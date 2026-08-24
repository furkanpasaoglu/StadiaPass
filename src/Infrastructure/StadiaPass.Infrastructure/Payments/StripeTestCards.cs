using System.Collections.Frozen;

namespace StadiaPass.Infrastructure.Payments;

/// <summary>
/// Stripe refuses raw card numbers - "Sending credit card numbers directly to the Stripe API is generally
/// unsafe" - unless an account is specifically approved for it, and rightly so: a real integration tokenises
/// the card in the browser and the server never sees it. For testing without a browser, Stripe publishes a
/// payment method token per test card, which is what this table maps onto. The card a customer types is
/// therefore never sent anywhere; only the token that stands for it is.
/// </summary>
internal static class StripeTestCards
{
    private static readonly FrozenDictionary<string, string> TokensByNumber =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["4242424242424242"] = "pm_card_visa",
            ["5555555555554444"] = "pm_card_mastercard",
            ["378282246310005"] = "pm_card_amex",
            ["4000000000009995"] = "pm_card_chargeDeclinedInsufficientFunds",
            ["4000000000000002"] = "pm_card_chargeDeclined",
            ["4000000000000069"] = "pm_card_chargeDeclinedExpiredCard",
            ["4000000000000127"] = "pm_card_chargeDeclinedIncorrectCvc",
            ["4000000000009987"] = "pm_card_chargeDeclinedLostCard",
            ["4000000000000119"] = "pm_card_chargeDeclinedProcessingError"
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string? TokenFor(string cardNumber) =>
        TokensByNumber.TryGetValue(cardNumber, out var token) ? token : null;
}
