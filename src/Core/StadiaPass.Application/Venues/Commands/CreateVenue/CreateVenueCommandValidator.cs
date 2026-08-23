using FluentValidation;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Venues.Commands.CreateVenue;

internal sealed class CreateVenueCommandValidator : AbstractValidator<CreateVenueCommand>
{
    public CreateVenueCommandValidator()
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
            .Must(VenueBlockRules.FitsCapacity)
            .WithMessage($"A venue seating plan is limited to {Venue.MaxCapacity} seats.")
            .When(command => command.Blocks is { Count: > 0 });

        RuleFor(command => command.Blocks)
            .Must(VenueBlockRules.HasUniqueNames)
            .WithMessage("Block names must be unique within a venue.")
            .When(command => command.Blocks is { Count: > 0 });

        RuleForEach(command => command.Blocks).SetValidator(new VenueBlockInputValidator());
    }
}

internal sealed class VenueBlockInputValidator : AbstractValidator<VenueBlockInput>
{
    public VenueBlockInputValidator()
    {
        RuleFor(block => block.Name)
            .NotEmpty().WithMessage("Block name is required.")
            .MaximumLength(10).WithMessage("Block name cannot exceed 10 characters.")
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Block name may only contain letters, digits and hyphens.");

        RuleFor(block => block.RowCount)
            .InclusiveBetween(1, 500).WithMessage("Row count must be between 1 and 500.");

        RuleFor(block => block.SeatsPerRow)
            .InclusiveBetween(1, 500).WithMessage("Seats per row must be between 1 and 500.");

        RuleFor(block => block.PriceMultiplier)
            .InclusiveBetween(0.01m, 20m).WithMessage("Price multiplier must be between 0.01 and 20.");
    }
}

internal static class VenueBlockRules
{
    public static bool FitsCapacity(IReadOnlyList<VenueBlockInput> blocks) =>
        blocks.Sum(block => (long)block.RowCount * block.SeatsPerRow) <= Venue.MaxCapacity;

    public static bool HasUniqueNames(IReadOnlyList<VenueBlockInput> blocks) =>
        blocks.Select(block => block.Name?.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Count() == blocks.Count;
}
