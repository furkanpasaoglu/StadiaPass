using FluentValidation;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.Identity.Roles.Commands.UpdateRolePermissions;

internal sealed class UpdateRolePermissionsCommandValidator : AbstractValidator<UpdateRolePermissionsCommand>
{
    public UpdateRolePermissionsCommandValidator()
    {
        RuleFor(command => command.RoleName)
            .NotEmpty().WithMessage("Role name is required.")
            .Must(name => !StadiaPassPermissions.IsPermissionRole(name))
            .WithMessage("A permission role cannot be edited from the role portal.");

        RuleForEach(command => command.Permissions)
            .Must(StadiaPassPermissions.IsDefined)
            .WithMessage("'{PropertyValue}' is not a permission this application declares.");
    }
}
