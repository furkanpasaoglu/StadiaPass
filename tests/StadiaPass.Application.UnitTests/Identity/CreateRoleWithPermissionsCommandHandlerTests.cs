using NSubstitute;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Identity;
using StadiaPass.Application.Identity.Roles.Commands.CreateRoleWithPermissions;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.Application.UnitTests.Identity;

/// <summary>
/// A business role is a composite of permission roles. Creating one has to bind exactly the permissions the
/// catalogue defines - inventing realm roles for names it does not recognise would fill the realm with
/// entries no policy will ever ask for, and dropping the resolution step would produce a role that grants
/// nothing while the portal shows it as granting everything asked for.
/// </summary>
public sealed class CreateRoleWithPermissionsCommandHandlerTests
{
    private const string BusinessRole = "BoxOffice";

    private const string MatchesCreate = StadiaPassPermissions.Matches.Create;

    private const string TicketsView = StadiaPassPermissions.Tickets.View;

    private readonly IKeycloakAdminService _keycloak = Substitute.For<IKeycloakAdminService>();

    private readonly CreateRoleWithPermissionsCommandHandler _handler;

    public CreateRoleWithPermissionsCommandHandlerTests()
    {
        _keycloak.FindRealmRoleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((KeycloakRole?)null);
        _keycloak.GetRealmRolesAsync(Arg.Any<CancellationToken>()).Returns([]);

        _keycloak
            .CreateRealmRoleAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => ARole(call.ArgAt<string>(0)));

        _handler = new CreateRoleWithPermissionsCommandHandler(_keycloak);
    }

    [Fact]
    public async Task Should_Throw_When_TheRealmAlreadyHasARoleWithThatName()
    {
        // Arrange
        _keycloak.FindRealmRoleAsync(BusinessRole, Arg.Any<CancellationToken>()).Returns(ARole(BusinessRole));

        // Act
        var creating = async () => await _handler.Handle(ACommand(MatchesCreate), CancellationToken.None);

        // Assert - without this the second create silently rewrites whatever the first one was granting.
        await creating.Should().ThrowAsync<ConflictException>();
        await _keycloak
            .DidNotReceive()
            .CreateRealmRoleAsync(BusinessRole, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_CreateThePermissionRole_When_TheRealmDoesNotCarryItYet()
    {
        // Act
        await _handler.Handle(ACommand(MatchesCreate), CancellationToken.None);

        // Assert - a permission constant added in code has to become usable without anybody opening Keycloak,
        // otherwise the catalogue and the realm drift apart and the new checkbox grants nothing.
        await _keycloak.Received(1).CreateRealmRoleAsync(MatchesCreate, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReuseThePermissionRole_When_TheRealmAlreadyCarriesIt()
    {
        // Arrange
        _keycloak.GetRealmRolesAsync(Arg.Any<CancellationToken>()).Returns([ARole(MatchesCreate)]);

        // Act
        await _handler.Handle(ACommand(MatchesCreate), CancellationToken.None);

        // Assert - creating it again would either fail against the provider or split one permission across two
        // realm roles, and only one of them would be bound to anything.
        await _keycloak
            .DidNotReceive()
            .CreateRealmRoleAsync(MatchesCreate, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_IgnoreAName_When_TheCatalogueDoesNotDefineItAsAPermission()
    {
        // Act
        var role = await _handler.Handle(ACommand(MatchesCreate, "StadiaPass.Matches.Destroy"), CancellationToken.None);

        // Assert - a typed or stale permission name must not become a realm role of its own, because nothing
        // will ever ask for it and it will sit there looking like a granted capability.
        role.Permissions.Should().BeEquivalentTo([MatchesCreate]);
        await _keycloak
            .DidNotReceive()
            .CreateRealmRoleAsync("StadiaPass.Matches.Destroy", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_BindThePermissionsToTheNewRole()
    {
        // Act
        var role = await _handler.Handle(ACommand(TicketsView, MatchesCreate), CancellationToken.None);

        // Assert - the composites are what turn the role into a grant; without them the role exists and does
        // nothing. The order is fixed so the portal does not reshuffle the list between reads.
        await _keycloak.Received(1).AddRoleCompositesAsync(
            "id-" + BusinessRole,
            Arg.Is<IReadOnlyCollection<KeycloakRole>>(composites => composites.Count == 2),
            Arg.Any<CancellationToken>());
        role.Permissions.Should().Equal(MatchesCreate, TicketsView);
    }

    [Fact]
    public async Task Should_BindNothing_When_NoRecognisedPermissionWasAskedFor()
    {
        // Act
        var role = await _handler.Handle(ACommand("not.a.permission"), CancellationToken.None);

        // Assert - an empty composite call is rejected by the provider, so the handler has to skip it.
        await _keycloak.DidNotReceive().AddRoleCompositesAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyCollection<KeycloakRole>>(), Arg.Any<CancellationToken>());
        role.Permissions.Should().BeEmpty();
    }

    private static KeycloakRole ARole(string name) => new("id-" + name, name, null, Composite: false);

    private static CreateRoleWithPermissionsCommand ACommand(params string[] permissions) =>
        new(BusinessRole, "the counter staff", permissions);
}
