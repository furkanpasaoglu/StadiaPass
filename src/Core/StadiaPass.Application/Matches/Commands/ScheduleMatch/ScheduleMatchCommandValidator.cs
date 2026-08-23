using FluentValidation;

namespace StadiaPass.Application.Matches.Commands.ScheduleMatch;

internal sealed class ScheduleMatchCommandValidator : AbstractValidator<ScheduleMatchCommand>
{
    public ScheduleMatchCommandValidator()
    {
        RuleFor(command => command.HomeTeam)
            .NotEmpty().WithMessage("Home team is required.")
            .MaximumLength(80).WithMessage("Home team cannot exceed 80 characters.");

        RuleFor(command => command.AwayTeam)
            .NotEmpty().WithMessage("Away team is required.")
            .MaximumLength(80).WithMessage("Away team cannot exceed 80 characters.")
            .NotEqual(command => command.HomeTeam, StringComparer.OrdinalIgnoreCase)
            .WithMessage("A team cannot play against itself.");

        RuleFor(command => command.Stadium)
            .NotEmpty().WithMessage("Stadium is required.")
            .MaximumLength(120).WithMessage("Stadium cannot exceed 120 characters.");

        RuleFor(command => command.KickOffUtc)
            .GreaterThan(DateTimeOffset.UtcNow).WithMessage("Kick-off must be in the future.");

        RuleFor(command => command.Capacity)
            .InclusiveBetween(1, 250_000).WithMessage("Capacity must be between 1 and 250000.");
    }
}
