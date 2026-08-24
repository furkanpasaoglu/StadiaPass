using FluentValidation;
using StadiaPass.Application.Common.Abstractions;

namespace StadiaPass.Application.Tickets.Commands.ConfirmTicketPurchase;

/// <summary>
/// The card is checked here so an obviously wrong number never reaches a provider - a typo should cost a
/// round trip to the browser, not a declined charge on the customer's statement.
/// </summary>
internal sealed class ConfirmTicketPurchaseCommandValidator : AbstractValidator<ConfirmTicketPurchaseCommand>
{
    public ConfirmTicketPurchaseCommandValidator(IDateTimeProvider dateTimeProvider)
    {
        RuleFor(command => command.MatchId)
            .NotEmpty().WithMessage("A match identifier is required.");

        RuleFor(command => command.SeatNumber)
            .NotEmpty().WithMessage("A seat number is required.")
            .Matches("^[A-Za-z0-9-]+-[0-9]+-[0-9]+$")
            .WithMessage("Seat number must look like BLOCK-ROW-NUMBER, for example A1-12-7.");

        RuleFor(command => command.CardHolderName)
            .NotEmpty().WithMessage("The name printed on the card is required.")
            .MaximumLength(128).WithMessage("The card holder name is too long.");

        RuleFor(command => command.CardNumber)
            .NotEmpty().WithMessage("A card number is required.")
            .Must(HasCardNumberLength).WithMessage("A card number has between 13 and 19 digits.")
            .Must(PassesLuhn).WithMessage("That card number is not valid.");

        RuleFor(command => command.ExpirationMonth)
            .InclusiveBetween(1, 12).WithMessage("The expiry month must be between 1 and 12.");

        RuleFor(command => command.ExpirationYear)
            .GreaterThanOrEqualTo(_ => dateTimeProvider.UtcNow.Year)
            .WithMessage("The card has expired.")
            .LessThanOrEqualTo(_ => dateTimeProvider.UtcNow.Year + 20)
            .WithMessage("That expiry year is not plausible.");

        RuleFor(command => command)
            .Must(NotBeExpired(dateTimeProvider))
            .WithName(nameof(ConfirmTicketPurchaseCommand.ExpirationMonth))
            .WithMessage("The card has expired.");

        RuleFor(command => command.Cvv)
            .NotEmpty().WithMessage("The security code is required.")
            .Matches("^[0-9]{3,4}$").WithMessage("The security code is three or four digits.");
    }

    private static bool HasCardNumberLength(string cardNumber) =>
        Digits(cardNumber).Length is >= 13 and <= 19;

    /// <summary>
    /// The check digit every card number carries. It catches a mistyped digit or two transposed ones, which
    /// is the overwhelming majority of what a customer actually gets wrong.
    /// </summary>
    private static bool PassesLuhn(string cardNumber)
    {
        var digits = Digits(cardNumber);

        if (digits.Length is 0)
        {
            return false;
        }

        var sum = 0;
        var doubling = false;

        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var value = digits[index] - '0';

            if (doubling)
            {
                value *= 2;

                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            doubling = !doubling;
        }

        return sum % 10 is 0;
    }

    /// <summary>A card is good through the last day of its expiry month.</summary>
    private static Func<ConfirmTicketPurchaseCommand, bool> NotBeExpired(IDateTimeProvider dateTimeProvider) =>
        command =>
        {
            if (command.ExpirationMonth is < 1 or > 12)
            {
                return true;
            }

            var now = dateTimeProvider.UtcNow;

            return command.ExpirationYear > now.Year
                   || (command.ExpirationYear == now.Year && command.ExpirationMonth >= now.Month);
        };

    private static string Digits(string value) => string.Concat(value.Where(char.IsAsciiDigit));
}
