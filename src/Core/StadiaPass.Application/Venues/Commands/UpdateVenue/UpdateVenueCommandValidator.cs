using FluentValidation;
using StadiaPass.Application.Venues.Commands.CreateVenue;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Venues.Commands.UpdateVenue;

internal sealed class UpdateVenueCommandValidator : AbstractValidator<UpdateVenueCommand>
{
    public UpdateVenueCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty().WithMessage("A venue identifier is required.");

        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("Venue name is required.")
            .MaximumLength(120).WithMessage("Venue name cannot exceed 120 characters.");

        RuleFor(command => command.City)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(80).WithMessage("City cannot exceed 80 characters.");

        RuleFor(command => command.Kind)
            .Must(kind => Enum.TryParse<VenueKind>(kind, ignoreCase: true, out _))
            .WithMessage($"Kind must be one of: {string.Join(", ", Enum.GetNames<VenueKind>())}.");

        When(command => command.Blocks is { Count: > 0 }, () =>
        {
            RuleFor(command => command.Blocks!)
                .Must(VenueBlockRules.FitsCapacity)
                .WithMessage($"A venue seating plan is limited to {Venue.MaxCapacity} seats.")
                .Must(VenueBlockRules.HasUniqueNames)
                .WithMessage("Block names must be unique within a venue.");

            RuleForEach(command => command.Blocks!).SetValidator(new VenueBlockInputValidator());
        });
    }
}
