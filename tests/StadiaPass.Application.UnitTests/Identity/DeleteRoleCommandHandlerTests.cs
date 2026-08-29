using NSubstitute;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Identity;
using StadiaPass.Application.Identity.Roles.Commands.DeleteRole;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.UnitTests.Identity;

/// <summary>
/// Permission roles are the application's own vocabulary living in the realm. The portal only ever lists
/// business roles, but the command takes a name, and a name is all anybody needs to send.
/// </summary>
public sealed class DeleteRoleCommandHandlerTests
{
    private readonly IKeycloakAdminService _keycloak = Substitute.For<IKeycloakAdminService>();

    private readonly DeleteRoleCommandHandler _handler;

    public DeleteRoleCommandHandlerTests() => _handler = new DeleteRoleCommandHandler(_keycloak);

    [Fact]
    public async Task Should_RefuseToDeleteAPermissionRole()
    {
        // Arrange
        _keycloak
            .FindRealmRoleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new KeycloakRole("id", StadiaPassPermissions.Matches.Create, null, Composite: false));

        // Act
        var deleting = async () =>
            await _handler.Handle(new DeleteRoleCommand(StadiaPassPermissions.Matches.Create), CancellationToken.None);

        // Assert - deleting one of these takes a capability away from every business role composed of it at
        // once, and nothing in the application would report the grant as missing rather than as denied.
        await deleting.Should().ThrowAsync<ConflictException>();
        await _keycloak.DidNotReceive().DeleteRealmRoleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheRoleIsNotInTheRealm()
    {
        // Arrange
        _keycloak
            .FindRealmRoleAsync("BoxOffice", Arg.Any<CancellationToken>())
            .Returns((KeycloakRole?)null);

        // Act
        var deleting = async () => await _handler.Handle(new DeleteRoleCommand("BoxOffice"), CancellationToken.None);

        // Assert - the provider answers a delete of something absent with a 404 that would surface as a 500.
        await deleting.Should().ThrowAsync<NotFoundException>();
        await _keycloak.DidNotReceive().DeleteRealmRoleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_DeleteABusinessRole()
    {
        // Arrange
        _keycloak
            .FindRealmRoleAsync("BoxOffice", Arg.Any<CancellationToken>())
            .Returns(new KeycloakRole("id-BoxOffice", "BoxOffice", null, Composite: true));

        // Act
        await _handler.Handle(new DeleteRoleCommand("BoxOffice"), CancellationToken.None);

        // Assert
        await _keycloak.Received(1).DeleteRealmRoleAsync("BoxOffice", Arg.Any<CancellationToken>());
    }
}
