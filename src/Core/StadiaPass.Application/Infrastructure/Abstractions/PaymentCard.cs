namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// The card presented at checkout. Nothing in StadiaPass persists these values: they travel from the form to
/// the payment provider and are gone with the request. <see cref="ToString"/> is overridden - and the number
/// and the security code are named so the log destructuring policy masks them - because the surest way to
/// leak a card is for something to render one by accident.
/// </summary>
public sealed record PaymentCard(
    string CardHolderName,
    string CardNumber,
    int ExpirationMonth,
    int ExpirationYear,
    string Cvv)
{
    /// <summary>What a receipt is allowed to show: the last four digits and nothing else.</summary>
    public string MaskedNumber =>
        CardNumber is { Length: >= 4 } number ? $"**** **** **** {number[^4..]}" : "****";

    public override string ToString() => MaskedNumber;
}
