using FluentValidation;

namespace StadiaPass.Application.Identity.Users.Commands.UpdateUserRoles;

/// <summary>
/// The guard the role portal has had all along, on the side that hands roles to people.
/// </summary>
/// <remarks>
/// Without it this command was the one way round the two-layer identity model: whoever could manage users
/// could put a permission role directly on somebody - their own account included - and collect any
/// permission in the catalogue without ever touching a business role. The read side would then show them
/// holding a role it does not list.
/// </remarks>
internal sealed class UpdateUserRolesCommandValidator : AbstractValidator<UpdateUserRolesCommand>
{
    public UpdateUserRolesCommandValidator()
    {
        RuleFor(command => command.UserId)
            .NotEmpty().WithMessage("User id is required.");

        RuleForEach(command => command.Roles)
            .Must(BusinessRoles.IsAssignableToUser)
            .WithMessage(BusinessRoles.RefusalMessage);
    }
}
