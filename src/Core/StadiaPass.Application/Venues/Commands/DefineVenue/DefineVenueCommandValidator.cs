using FluentValidation;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Venues.Commands.DefineVenue;

internal sealed class DefineVenueCommandValidator : AbstractValidator<DefineVenueCommand>
{
    public DefineVenueCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("Venue name is required.")
            .MaximumLength(120).WithMessage("Venue name cannot exceed 120 characters.");

        RuleFor(command => command.City)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(80).WithMessage("City cannot exceed 80 characters.");

        RuleFor(command => command.Kind)
            .Must(kind => Enum.TryParse<VenueKind>(kind, ignoreCase: true, out _))
            .WithMessage($"Kind must be one of: {string.Join(", ", Enum.GetNames<VenueKind>())}.");

        RuleFor(command => command.Blocks)
            .NotEmpty().WithMessage("At least one seating block is required.");

        RuleFor(command => command.Blocks)
            .Must(blocks => blocks.Sum(block => block.RowCount * block.SeatsPerRow) <= Venue.MaxCapacity)
            .WithMessage($"A venue seating plan is limited to {Venue.MaxCapacity} seats.")
            .When(command => command.Blocks is { Count: > 0 });

        RuleForEach(command => command.Blocks).ChildRules(block =>
        {
            block.RuleFor(item => item.Name)
                .NotEmpty().WithMessage("Block name is required.")
                .MaximumLength(10).WithMessage("Block name cannot exceed 10 characters.")
                .Matches("^[A-Za-z0-9-]+$").WithMessage("Block name may only contain letters, digits and hyphens.");

            block.RuleFor(item => item.RowCount)
                .InclusiveBetween(1, 500).WithMessage("Row count must be between 1 and 500.");

            block.RuleFor(item => item.SeatsPerRow)
                .InclusiveBetween(1, 500).WithMessage("Seats per row must be between 1 and 500.");

            block.RuleFor(item => item.PriceMultiplier)
                .InclusiveBetween(0.01m, 20m).WithMessage("Price multiplier must be between 0.01 and 20.");
        });
    }
}
