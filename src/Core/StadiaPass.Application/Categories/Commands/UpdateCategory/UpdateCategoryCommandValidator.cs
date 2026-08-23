using FluentValidation;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Categories.Commands.UpdateCategory;

internal sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty().WithMessage("A category identifier is required.");

        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("Category name is required.")
            .MaximumLength(60).WithMessage("Category name cannot exceed 60 characters.");

        RuleFor(command => command.Description)
            .MaximumLength(255).WithMessage("Description cannot exceed 255 characters.");

        RuleFor(command => command.AllowedVenueKinds)
            .NotEmpty().WithMessage("Pick at least one kind of venue this sport can be played in.");

        RuleForEach(command => command.AllowedVenueKinds)
            .Must(kind => Enum.TryParse<VenueKind>(kind, ignoreCase: true, out _))
            .WithMessage($"Venue kind must be one of: {string.Join(", ", Enum.GetNames<VenueKind>())}.");
    }
}
