using FluentAssertions;
using StadiaPass.Application.Identity.Users.Commands.CreateUser;
using StadiaPass.Application.Identity.Users.Commands.UpdateUserRoles;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.UnitTests.Identity;

/// <summary>
/// Permissions are the building blocks and business roles are what a person is given. Every read path in the
/// portal says so; until these rules existed, the two write paths that put roles on a user did not, and
/// whoever could manage users could give themselves any permission in the catalogue directly.
/// </summary>
public sealed class UserRoleAssignmentTests
{
    private readonly UpdateUserRolesCommandValidator _updateRoles = new();

    private readonly CreateUserCommandValidator _createUser = new();

    [Fact]
    public void UpdateUserRoles_RefusesAPermissionRole()
    {
        var result = _updateRoles.Validate(
            new UpdateUserRolesCommand("user-id", [StadiaPassPermissions.Users.Manage]));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateUserRoles_RefusesAKeycloakRoleOfItsOwn()
    {
        // The read side already skips these when working out what to remove, so one assigned here would
        // never be taken off again.
        var result = _updateRoles.Validate(new UpdateUserRolesCommand("user-id", ["offline_access"]));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateUserRoles_AllowsABusinessRole()
    {
        var result = _updateRoles.Validate(new UpdateUserRolesCommand("user-id", ["BoxOffice"]));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateUser_RefusesAPermissionRole()
    {
        // Creating an account is the other door into the same room.
        var result = _createUser.Validate(new CreateUserCommand(
            "auditor",
            "auditor@stadiapass.local",
            FirstName: null,
            LastName: null,
            "correct horse battery",
            [StadiaPassPermissions.Roles.Manage]));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateUser_AllowsABusinessRole()
    {
        var result = _createUser.Validate(new CreateUserCommand(
            "auditor",
            "auditor@stadiapass.local",
            FirstName: null,
            LastName: null,
            "correct horse battery",
            ["Viewer"]));

        result.IsValid.Should().BeTrue();
    }
}
