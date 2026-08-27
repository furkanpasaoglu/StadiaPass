using FluentValidation;

namespace StadiaPass.Application.Identity.Users.Commands.CreateUser;

internal sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(command => command.Username)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(64).WithMessage("Username cannot exceed 64 characters.")
            .Matches("^[a-z0-9._-]+$")
            .WithMessage("Username may contain lowercase letters, digits, dot, dash and underscore only.");

        RuleFor(command => command.Email)
            .EmailAddress().WithMessage("Email is not valid.")
            .When(command => !string.IsNullOrWhiteSpace(command.Email));

        RuleFor(command => command.FirstName).MaximumLength(64);
        RuleFor(command => command.LastName).MaximumLength(64);

        RuleFor(command => command.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters.")
            .MaximumLength(128).WithMessage("Password cannot exceed 128 characters.");

        // A new account is the other door into the same room; see UpdateUserRolesCommandValidator.
        RuleForEach(command => command.Roles)
            .Must(BusinessRoles.IsAssignableToUser)
            .WithMessage(BusinessRoles.RefusalMessage);
    }
}
