using NSubstitute;
using StadiaPass.Application.Categories.Commands.UpdateCategory;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Categories;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.UnitTests.Categories;

/// <summary>
/// Retiring a sport is done by deactivating it rather than deleting it, because the fixtures already opened
/// under it have to keep selling. The uniqueness check is the subtle one: it has to exclude the row being
/// edited, or a category can never be saved under the name it already has.
/// </summary>
public sealed class UpdateCategoryCommandHandlerTests
{
    private readonly ISportCategoryRepository _categoryRepository = Substitute.For<ISportCategoryRepository>();

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly UpdateCategoryCommandHandler _handler;

    private readonly SportCategory _category = SportCategory.Define("Football", null, [VenueKind.Stadium]);

    public UpdateCategoryCommandHandlerTests()
    {
        _categoryRepository
            .GetByIdAsync(_category.Id, Arg.Any<CancellationToken>())
            .Returns(_category);

        _handler = new UpdateCategoryCommandHandler(_categoryRepository, _matchRepository, _unitOfWork);
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheCategoryIsGone()
    {
        // Arrange
        _categoryRepository
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((SportCategory?)null);

        // Act
        var updating = async () => await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert
        await updating.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_ExcludeTheCategoryItselfFromTheNameCheck()
    {
        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - asking whether the name exists without excluding this row means the category always
        // collides with itself, and nothing about it can ever be saved again.
        await _categoryRepository.Received(1).ExistsAsync("Football", _category.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Throw_When_AnotherCategoryAlreadyHasTheName()
    {
        // Arrange
        _categoryRepository
            .ExistsAsync("Football", _category.Id, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var updating = async () => await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert
        await updating.Should().ThrowAsync<ConflictException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RetireTheCategory_When_ItIsSavedInactive()
    {
        // Act
        var category = await _handler.Handle(ACommand(isActive: false), CancellationToken.None);

        // Assert - this is the way a sport is taken out of use: no new fixture may be opened under it, and the
        // ones already selling are left alone.
        category.IsActive.Should().BeFalse();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_BringTheCategoryBack_When_ItIsSavedActive()
    {
        // Arrange
        _category.Deactivate();

        // Act
        var category = await _handler.Handle(ACommand(isActive: true), CancellationToken.None);

        // Assert
        category.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Should_ReplaceTheVenueKindsItMayBePlayedIn()
    {
        // Act
        var category = await _handler.Handle(ACommand(venueKinds: ["Arena"]), CancellationToken.None);

        // Assert - replaced rather than added to, so a kind removed from the form is removed from the rule.
        category.AllowedVenueKinds.Should().Equal("Arena");
    }

    [Fact]
    public async Task Should_RefuseToNarrowTheVenueKinds_When_FixturesWereOpenedUnderIt()
    {
        // Arrange
        _matchRepository.ExistsForCategoryAsync(_category.Id, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var updating = async () => await _handler.Handle(ACommand(venueKinds: ["Arena"]), CancellationToken.None);

        // Assert - deleting the category is refused while fixtures exist, but taking away the kind of building
        // those fixtures are played in was not, and it leaves exactly the same contradiction: a match whose own
        // category says it cannot be played where it is. Nothing rechecks that rule after creation.
        await updating.Should().ThrowAsync<ConflictException>();
        _category.AllowedVenueKinds.Should().Equal(VenueKind.Stadium);
    }

    [Fact]
    public async Task Should_AllowAnAdditionalVenueKind_When_FixturesWereOpenedUnderIt()
    {
        // Arrange
        _matchRepository.ExistsForCategoryAsync(_category.Id, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var category = await _handler.Handle(ACommand(venueKinds: ["Stadium", "Arena"]), CancellationToken.None);

        // Assert - widening the rule cannot invalidate a fixture that already satisfies it, so it stays
        // allowed. Refusing every edit would make a live category impossible to extend.
        category.AllowedVenueKinds.Should().Equal("Arena", "Stadium");
    }

    [Fact]
    public async Task Should_NotAskAboutFixtures_When_TheVenueKindsAreUnchanged()
    {
        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - renaming or retiring a category is not what puts a fixture in the wrong building.
        await _matchRepository.DidNotReceive().ExistsForCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private UpdateCategoryCommand ACommand(bool isActive = true, IReadOnlyList<string>? venueKinds = null) =>
        new(_category.Id, "Football", "eleven a side", isActive, venueKinds ?? ["Stadium"]);
}
