using NSubstitute;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Identity;
using StadiaPass.Application.Identity.Roles.Commands.UpdateRolePermissions;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.UnitTests.Identity;

/// <summary>
/// Editing what a business role grants is a set difference against what it already grants, and the realm is
/// the only place those names live. Sending the whole desired set as additions, or the whole current set as
/// removals, would work by accident against a forgiving provider and destroy the role against a strict one.
/// </summary>
public sealed class UpdateRolePermissionsCommandHandlerTests
{
    private const string BusinessRole = "BoxOffice";

    private const string RoleId = "id-BoxOffice";

    private const string MatchesCreate = StadiaPassPermissions.Matches.Create;

    private const string TicketsView = StadiaPassPermissions.Tickets.View;

    private readonly IKeycloakAdminService _keycloak = Substitute.For<IKeycloakAdminService>();

    private readonly UpdateRolePermissionsCommandHandler _handler;

    public UpdateRolePermissionsCommandHandlerTests()
    {
        _keycloak
            .FindRealmRoleAsync(BusinessRole, Arg.Any<CancellationToken>())
            .Returns(new KeycloakRole(RoleId, BusinessRole, null, Composite: true));

        _keycloak
            .GetRealmRolesAsync(Arg.Any<CancellationToken>())
            .Returns([ARole(MatchesCreate), ARole(TicketsView)]);

        _handler = new UpdateRolePermissionsCommandHandler(_keycloak);
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheRoleIsNotInTheRealm()
    {
        // Arrange
        _keycloak.FindRealmRoleAsync(BusinessRole, Arg.Any<CancellationToken>()).Returns((KeycloakRole?)null);

        // Act
        var updating = async () => await _handler.Handle(ACommand(MatchesCreate), CancellationToken.None);

        // Assert
        await updating.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_AddOnlyThePermissionTheRoleIsMissing()
    {
        // Arrange
        GivenTheRoleCurrentlyGrants(MatchesCreate);

        // Act
        await _handler.Handle(ACommand(MatchesCreate, TicketsView), CancellationToken.None);

        // Assert - re-adding something already bound is at best a wasted call and at worst rejected, and it
        // hides the one addition that mattered.
        await _keycloak.Received(1).AddRoleCompositesAsync(
            RoleId,
            Arg.Is<IReadOnlyCollection<KeycloakRole>>(composites =>
                composites.Count == 1 && composites.Single().Name == TicketsView),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RemoveOnlyThePermissionNoLongerWanted()
    {
        // Arrange
        GivenTheRoleCurrentlyGrants(MatchesCreate, TicketsView);

        // Act
        await _handler.Handle(ACommand(MatchesCreate), CancellationToken.None);

        // Assert
        await _keycloak.Received(1).RemoveRoleCompositesAsync(
            RoleId,
            Arg.Is<IReadOnlyCollection<KeycloakRole>>(composites =>
                composites.Count == 1 && composites.Single().Name == TicketsView),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_LeaveCompositesThatAreNotPermissions_When_ThePermissionsAreEdited()
    {
        // Arrange - a business role may be composed of another business role. Only the permission layer is
        // being edited here.
        GivenTheRoleCurrentlyGrants(MatchesCreate);
        _keycloak
            .GetRoleCompositesAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns([ARole(MatchesCreate), ARole("Supervisor")]);

        // Act
        await _handler.Handle(ACommand(MatchesCreate), CancellationToken.None);

        // Assert - dropping the permission filter would make every edit of a checklist quietly unpick the role
        // nesting underneath it.
        await _keycloak.DidNotReceive().RemoveRoleCompositesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeycloakRole>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_TouchNothing_When_ThePermissionsAreUnchanged()
    {
        // Arrange
        GivenTheRoleCurrentlyGrants(MatchesCreate, TicketsView);

        // Act
        await _handler.Handle(ACommand(MatchesCreate, TicketsView), CancellationToken.None);

        // Assert - saving a form nobody changed must not send the provider an empty composite call, which it
        // rejects.
        await _keycloak.DidNotReceive().AddRoleCompositesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeycloakRole>>(), Arg.Any<CancellationToken>());
        await _keycloak.DidNotReceive().RemoveRoleCompositesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeycloakRole>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotAskForComposites_When_TheRoleIsNotACompositeYet()
    {
        // Arrange - a role that grants nothing yet has no composites to read, and Keycloak answers that
        // question with an error rather than an empty list.
        _keycloak
            .FindRealmRoleAsync(BusinessRole, Arg.Any<CancellationToken>())
            .Returns(new KeycloakRole(RoleId, BusinessRole, null, Composite: false));

        // Act
        var role = await _handler.Handle(ACommand(MatchesCreate), CancellationToken.None);

        // Assert
        await _keycloak.DidNotReceive().GetRoleCompositesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        role.Permissions.Should().Equal(MatchesCreate);
    }

    private void GivenTheRoleCurrentlyGrants(params string[] permissions) =>
        _keycloak
            .GetRoleCompositesAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns([.. permissions.Select(ARole)]);

    private static KeycloakRole ARole(string name) => new("id-" + name, name, null, Composite: false);

    private static UpdateRolePermissionsCommand ACommand(params string[] permissions) =>
        new(BusinessRole, permissions);
}
