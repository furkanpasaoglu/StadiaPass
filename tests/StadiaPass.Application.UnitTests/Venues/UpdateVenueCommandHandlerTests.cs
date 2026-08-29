using NSubstitute;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Venues.Commands.CreateVenue;
using StadiaPass.Application.Venues.Commands.UpdateVenue;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.UnitTests.Venues;

/// <summary>
/// Editing a venue splits into two very different things. Its name and city are labels and may be corrected
/// at any time; its blocks are the plan that open matches have already turned into numbered seats, and
/// reshaping those under a live fixture would leave tickets pointing at places that no longer exist.
/// </summary>
public sealed class UpdateVenueCommandHandlerTests
{
    private readonly IVenueRepository _venueRepository = Substitute.For<IVenueRepository>();

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly UpdateVenueCommandHandler _handler;

    private readonly Venue _venue = TestData.Stadium();

    public UpdateVenueCommandHandlerTests()
    {
        _venueRepository
            .GetTrackedWithBlocksAsync(_venue.Id, Arg.Any<CancellationToken>())
            .Returns(_venue);

        _handler = new UpdateVenueCommandHandler(_venueRepository, _matchRepository, _unitOfWork);
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheVenueIsGone()
    {
        // Arrange
        _venueRepository
            .GetTrackedWithBlocksAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Venue?)null);

        // Act
        var updating = async () => await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert
        await updating.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_CorrectTheNameAndCity_When_ThePlanIsLeftAlone()
    {
        // Act
        var venue = await _handler.Handle(ACommand(name: "Ulker Stadyumu", city: "Istanbul"), CancellationToken.None);

        // Assert
        venue.Name.Should().Be("Ulker Stadyumu");
        venue.Blocks.Should().HaveCount(1);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotAskWhetherMatchesAreOpen_When_ThePlanIsNotBeingChanged()
    {
        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - making the question unconditional would make a venue unrenameable for as long as it has a
        // single fixture, which is most of the time.
        await _matchRepository.DidNotReceive().ExistsForVenueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RefuseToReshapeThePlan_When_AMatchIsOpenAgainstTheVenue()
    {
        // Arrange
        _matchRepository.ExistsForVenueAsync(_venue.Id, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var updating = async () => await _handler.Handle(ACommandReplacingTheBlocks(), CancellationToken.None);

        // Assert - the seats of an open fixture were numbered from the blocks as they are now; replacing them
        // would leave sold tickets pointing at places the building no longer has.
        await updating.Should().ThrowAsync<ConflictException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_LeaveTheVenueUntouched_When_ThePlanCannotBeReshaped()
    {
        // Arrange
        _matchRepository.ExistsForVenueAsync(_venue.Id, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var updating = async () =>
            await _handler.Handle(ACommandReplacingTheBlocks(name: "Ulker Stadyumu"), CancellationToken.None);

        // Assert - the aggregate was loaded tracked, so anything applied before the refusal stays in the change
        // tracker. Nothing saves it in a request that ends here, but the next thing to call SaveChanges in the
        // same scope would, and that is a bug this application has already paid for once.
        await updating.Should().ThrowAsync<ConflictException>();
        _venue.Name.Should().Be("Sukru Saracoglu");
    }

    [Fact]
    public async Task Should_RefuseToChangeTheKind_When_AMatchIsOpenAgainstTheVenue()
    {
        // Arrange
        _matchRepository.ExistsForVenueAsync(_venue.Id, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var updating = async () => await _handler.Handle(ACommand(kind: "Arena"), CancellationToken.None);

        // Assert - a category is only allowed to open a fixture in certain kinds of building, and that is
        // checked once, at creation. Changing the kind underneath an open fixture leaves it in a venue its own
        // category says it cannot be played in, and nothing rechecks.
        await updating.Should().ThrowAsync<ConflictException>();
        _venue.Kind.Should().Be(VenueKind.Stadium);
    }

    [Fact]
    public async Task Should_ReplaceThePlan_When_NoMatchIsOpenAgainstTheVenue()
    {
        // Arrange
        _matchRepository.ExistsForVenueAsync(_venue.Id, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var venue = await _handler.Handle(ACommandReplacingTheBlocks(), CancellationToken.None);

        // Assert - four rows of five, counted by hand.
        venue.Blocks.Should().ContainSingle().Which.Name.Should().Be("KALE");
        venue.Capacity.Should().Be(20);
    }

    private UpdateVenueCommand ACommand(
        string name = "Sukru Saracoglu",
        string city = "Istanbul",
        string kind = "Stadium") =>
        new(_venue.Id, name, city, kind);

    private UpdateVenueCommand ACommandReplacingTheBlocks(string name = "Sukru Saracoglu") =>
        new(
            _venue.Id,
            name,
            "Istanbul",
            "Stadium",
            [new VenueBlockInput("KALE", RowCount: 4, SeatsPerRow: 5)]);
}
