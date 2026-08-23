using FluentValidation;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Matches.Commands.CreateMatch;

internal sealed class CreateMatchCommandValidator : AbstractValidator<CreateMatchCommand>
{
    public CreateMatchCommandValidator()
    {
        RuleFor(command => command.Category)
            .Must(category => Enum.TryParse<SportCategory>(category, ignoreCase: true, out _))
            .WithMessage($"Category must be one of: {string.Join(", ", Enum.GetNames<SportCategory>())}.");

        RuleFor(command => command.VenueId)
            .NotEmpty().WithMessage("A venue is required.");

        RuleFor(command => command.HomeTeam)
            .NotEmpty().WithMessage("Home team is required.")
            .MaximumLength(80).WithMessage("Home team cannot exceed 80 characters.");

        RuleFor(command => command.AwayTeam)
            .NotEmpty().WithMessage("Away team is required.")
            .MaximumLength(80).WithMessage("Away team cannot exceed 80 characters.")
            .NotEqual(command => command.HomeTeam, StringComparer.OrdinalIgnoreCase)
            .WithMessage("A team cannot play against itself.");

        RuleFor(command => command.KickOffUtc)
            .GreaterThan(DateTimeOffset.UtcNow).WithMessage("Kick-off must be in the future.");

        RuleFor(command => command.BasePrice)
            .GreaterThan(0).WithMessage("Base price must be greater than zero.")
            .LessThanOrEqualTo(100_000).WithMessage("Base price exceeds the allowed maximum.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Base price supports at most 2 decimal places.");

        RuleFor(command => command.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Length(3).WithMessage("Currency must be a 3-letter ISO 4217 code.");
    }
}
