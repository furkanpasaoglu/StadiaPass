using FluentValidation;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.Identity.Roles.Commands.CreateRoleWithPermissions;

internal sealed class CreateRoleWithPermissionsCommandValidator
    : AbstractValidator<CreateRoleWithPermissionsCommand>
{
    public CreateRoleWithPermissionsCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("Role name is required.")
            .MaximumLength(64).WithMessage("Role name cannot exceed 64 characters.")
            .Matches("^[A-Za-z][A-Za-z0-9._-]*$")
            .WithMessage("Role name must start with a letter and may contain letters, digits, dot, dash and underscore.")
            .Must(name => !StadiaPassPermissions.IsPermissionRole(name))
            .WithMessage("That name is reserved for a permission and cannot be used as a business role.");

        RuleFor(command => command.Description)
            .MaximumLength(255).WithMessage("Description cannot exceed 255 characters.");

        RuleForEach(command => command.Permissions)
            .Must(StadiaPassPermissions.IsDefined)
            .WithMessage("'{PropertyValue}' is not a permission this application declares.");
    }
}
