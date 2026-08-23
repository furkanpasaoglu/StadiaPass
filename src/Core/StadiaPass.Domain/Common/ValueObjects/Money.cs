namespace StadiaPass.Domain.Common.ValueObjects;

public sealed record Money
{
    public const string DefaultCurrency = "TRY";

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero(string currency = DefaultCurrency) => new(decimal.Zero, currency);

    public static Money Create(decimal amount, string currency = DefaultCurrency) => amount switch
    {
        < 0 => throw new DomainRuleViolationException(
            "Money.NegativeAmount", "Monetary amount cannot be negative."),
        _ when string.IsNullOrWhiteSpace(currency) || currency.Length != 3 =>
            throw new DomainRuleViolationException(
                "Money.InvalidCurrency", "Currency must be a 3-letter ISO 4217 code."),
        _ => new Money(decimal.Round(amount, 2, MidpointRounding.ToEven), currency.ToUpperInvariant())
    };

    public Money Add(Money other) => other.Currency == Currency
        ? Create(Amount + other.Amount, Currency)
        : throw new DomainRuleViolationException(
            "Money.CurrencyMismatch", $"Cannot add {other.Currency} to {Currency}.");

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
