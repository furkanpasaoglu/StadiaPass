using NSubstitute;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Identity;
using StadiaPass.Application.Identity.Users.Commands.UpdateUserRoles;

namespace StadiaPass.Application.UnitTests.Identity;

/// <summary>
/// What a person holds is replaced with what the form said they should hold. The trap is that Keycloak keeps
/// its own roles on every account - <c>default-roles-stadiapass</c>, <c>offline_access</c> - and a set
/// difference that cannot tell them apart from ours will strip them the first time anybody edits a user.
/// </summary>
/// <remarks>
/// Which names may be handed to a person at all is enforced a layer earlier, by the validator; these cover
/// what the handler does with the names that get through. See <see cref="UserRoleAssignmentTests"/>.
/// </remarks>
public sealed class UpdateUserRolesCommandHandlerTests
{
    private const string UserId = "3f6c9f7a-1f0e-4a1b-9d2f-2f6f9b1c7a41";

    private readonly IKeycloakAdminService _keycloak = Substitute.For<IKeycloakAdminService>();

    private readonly UpdateUserRolesCommandHandler _handler;

    public UpdateUserRolesCommandHandlerTests()
    {
        _keycloak
            .GetUserAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new KeycloakUser(UserId, "furkan", "furkan@example.com", "Furkan", "Pasaoglu", Enabled: true));

        _keycloak
            .GetRealmRolesAsync(Arg.Any<CancellationToken>())
            .Returns([ARole("BoxOffice"), ARole("Supervisor")]);

        _handler = new UpdateUserRolesCommandHandler(_keycloak);
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheUserIsGone()
    {
        // Arrange
        _keycloak.GetUserAsync(UserId, Arg.Any<CancellationToken>()).Returns((KeycloakUser?)null);

        // Act
        var updating = async () => await _handler.Handle(ACommand("BoxOffice"), CancellationToken.None);

        // Assert
        await updating.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_AssignOnlyTheRoleThePersonDoesNotHave()
    {
        // Arrange
        GivenThePersonHolds("BoxOffice");

        // Act
        await _handler.Handle(ACommand("BoxOffice", "Supervisor"), CancellationToken.None);

        // Assert
        await _keycloak.Received(1).AssignUserRealmRolesAsync(
            UserId,
            Arg.Is<IReadOnlyCollection<KeycloakRole>>(roles => roles.Count == 1 && roles.Single().Name == "Supervisor"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_TakeBackOnlyTheRoleNoLongerWanted()
    {
        // Arrange
        GivenThePersonHolds("BoxOffice", "Supervisor");

        // Act
        await _handler.Handle(ACommand("BoxOffice"), CancellationToken.None);

        // Assert
        await _keycloak.Received(1).RemoveUserRealmRolesAsync(
            UserId,
            Arg.Is<IReadOnlyCollection<KeycloakRole>>(roles => roles.Count == 1 && roles.Single().Name == "Supervisor"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_LeaveKeycloaksOwnRolesOnTheAccount()
    {
        // Arrange - every account carries these and the form never lists them, so a naive difference sees them
        // as roles the administrator wants gone.
        GivenThePersonHolds("BoxOffice", "default-roles-stadiapass", "offline_access", "uma_authorization");

        // Act
        await _handler.Handle(ACommand("BoxOffice"), CancellationToken.None);

        // Assert - stripping default-roles is how an account stops being able to sign in at all, and the
        // administrator who did it was only editing a checkbox.
        await _keycloak.DidNotReceive().RemoveUserRealmRolesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeycloakRole>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_TouchNothing_When_TheRolesAreUnchanged()
    {
        // Arrange
        GivenThePersonHolds("BoxOffice");

        // Act
        var user = await _handler.Handle(ACommand("BoxOffice"), CancellationToken.None);

        // Assert - an empty assign or remove is rejected by the provider, so both calls have to be skipped.
        await _keycloak.DidNotReceive().AssignUserRealmRolesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeycloakRole>>(), Arg.Any<CancellationToken>());
        await _keycloak.DidNotReceive().RemoveUserRealmRolesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeycloakRole>>(), Arg.Any<CancellationToken>());
        user.Roles.Should().Equal("BoxOffice");
    }

    [Fact]
    public async Task Should_IgnoreARoleTheRealmDoesNotHave()
    {
        // Arrange - role names live only in the realm, so a stale name arriving from a form resolves to
        // nothing rather than to something invented on the spot.
        GivenThePersonHolds();

        // Act
        var user = await _handler.Handle(ACommand("Nightwatch"), CancellationToken.None);

        // Assert
        user.Roles.Should().BeEmpty();
        await _keycloak.DidNotReceive().AssignUserRealmRolesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeycloakRole>>(), Arg.Any<CancellationToken>());
    }

    private void GivenThePersonHolds(params string[] roleNames) =>
        _keycloak
            .GetUserRealmRolesAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([.. roleNames.Select(ARole)]);

    private static KeycloakRole ARole(string name) => new("id-" + name, name, null, Composite: false);

    private static UpdateUserRolesCommand ACommand(params string[] roles) => new(UserId, roles);
}
