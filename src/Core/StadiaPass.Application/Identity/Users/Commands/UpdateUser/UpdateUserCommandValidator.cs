using FluentValidation;

namespace StadiaPass.Application.Identity.Users.Commands.UpdateUser;

internal sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty().WithMessage("A user identifier is required.");

        RuleFor(command => command.Email)
            .EmailAddress().WithMessage("Email is not valid.")
            .When(command => !string.IsNullOrWhiteSpace(command.Email));

        RuleFor(command => command.FirstName).MaximumLength(64);
        RuleFor(command => command.LastName).MaximumLength(64);
    }
}
