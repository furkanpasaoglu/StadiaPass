using NSubstitute;
using StadiaPass.Application.Categories.Commands.DeleteCategory;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Categories;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.UnitTests.Categories;

/// <summary>
/// Deleting a category that fixtures were opened under is the destructive version of retiring it, and the
/// refusal has to say so - otherwise an administrator reaches for delete, is stopped by a foreign key they
/// cannot see, and never learns that deactivating is what they wanted.
/// </summary>
public sealed class DeleteCategoryCommandHandlerTests
{
    private readonly ISportCategoryRepository _categoryRepository = Substitute.For<ISportCategoryRepository>();

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly DeleteCategoryCommandHandler _handler;

    private readonly SportCategory _category = SportCategory.Define("Football", null, [VenueKind.Stadium]);

    public DeleteCategoryCommandHandlerTests()
    {
        _categoryRepository
            .GetByIdAsync(_category.Id, Arg.Any<CancellationToken>())
            .Returns(_category);

        _handler = new DeleteCategoryCommandHandler(_categoryRepository, _matchRepository, _unitOfWork);
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheCategoryIsAlreadyGone()
    {
        // Arrange
        _categoryRepository
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((SportCategory?)null);

        // Act
        var deleting = async () =>
            await _handler.Handle(new DeleteCategoryCommand(_category.Id), CancellationToken.None);

        // Assert
        await deleting.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_Refuse_When_AtLeastOneMatchWasOpenedUnderIt()
    {
        // Arrange
        _matchRepository.ExistsForCategoryAsync(_category.Id, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var deleting = async () =>
            await _handler.Handle(new DeleteCategoryCommand(_category.Id), CancellationToken.None);

        // Assert
        await deleting.Should().ThrowAsync<ConflictException>();
        _categoryRepository.DidNotReceive().Remove(Arg.Any<SportCategory>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RemoveTheCategory_When_NoMatchWasEverOpenedUnderIt()
    {
        // Arrange
        _matchRepository.ExistsForCategoryAsync(_category.Id, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        await _handler.Handle(new DeleteCategoryCommand(_category.Id), CancellationToken.None);

        // Assert
        _categoryRepository.Received(1).Remove(_category);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
