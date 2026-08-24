using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace StadiaPass.Infrastructure.Payments;

public enum PaymentProviderType
{
    /// <summary>Local simulation. No network call, no account, no key.</summary>
    Mock,

    /// <summary>Stripe's test API, reached with a test secret key.</summary>
    Stripe
}

public sealed class PaymentOptions
{
    public const string SectionName = "PaymentProvider";

    public PaymentProviderType Type { get; init; } = PaymentProviderType.Mock;

    /// <summary>
    /// Stripe test secret key, which starts with <c>sk_test_</c>. Only read when <see cref="Type"/> is
    /// <see cref="PaymentProviderType.Stripe"/>, so the mock runs on a clone with no configuration at all.
    /// </summary>
    public string? SecretKey { get; init; }

    /// <summary>
    /// The signing secret for the webhook endpoint, which starts with <c>whsec_</c>. It is what makes an
    /// anonymous public endpoint safe; without it nothing is accepted. Comes from Vault, and note that
    /// <c>stripe listen</c> prints a fresh one every time it starts - a fixed one comes from an endpoint
    /// defined in the Stripe dashboard.
    /// </summary>
    public string? WebhookSecret { get; init; }

    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// A missing key is only a problem for the provider that needs one, and it is a problem worth finding at
/// startup rather than at the first checkout.
/// </summary>
internal sealed class PaymentOptionsValidator : IValidateOptions<PaymentOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentOptions options)
    {
        if (options.Type is PaymentProviderType.Stripe && string.IsNullOrWhiteSpace(options.SecretKey))
        {
            return ValidateOptionsResult.Fail(
                $"{PaymentOptions.SectionName}:SecretKey is required when the provider is Stripe.");
        }

        if (options.Type is PaymentProviderType.Stripe
            && !options.SecretKey!.StartsWith("sk_test_", StringComparison.Ordinal))
        {
            // A live key in this project would charge real cards from a development machine.
            return ValidateOptionsResult.Fail(
                $"{PaymentOptions.SectionName}:SecretKey must be a Stripe test key (sk_test_...).");
        }

        return ValidateOptionsResult.Success;
    }
}
