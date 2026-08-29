using NSubstitute;
using StadiaPass.Application.Categories.Commands.CreateCategory;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Categories;

namespace StadiaPass.Application.UnitTests.Categories;

/// <summary>
/// A category says which kinds of building its sport can be played in, and the match aggregate refuses to
/// open a fixture that disagrees with it. Two categories under one name would make which of those rules
/// applies a matter of which row was read.
/// </summary>
public sealed class CreateCategoryCommandHandlerTests
{
    private readonly ISportCategoryRepository _categoryRepository = Substitute.For<ISportCategoryRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly CreateCategoryCommandHandler _handler;

    public CreateCategoryCommandHandlerTests() =>
        _handler = new CreateCategoryCommandHandler(_categoryRepository, _unitOfWork);

    [Fact]
    public async Task Should_Throw_When_TheNameIsAlreadyTaken()
    {
        // Arrange
        _categoryRepository
            .ExistsAsync("Football", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var creating = async () => await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert
        await creating.Should().ThrowAsync<ConflictException>();
        await _categoryRepository.DidNotReceive().AddAsync(Arg.Any<SportCategory>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_DefineTheCategoryWithTheVenueKindsItMayBePlayedIn()
    {
        // Act
        var category = await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - this is the list the match aggregate checks a venue against, so losing an entry makes
        // fixtures at that kind of building impossible to open.
        category.AllowedVenueKinds.Should().Equal("Arena", "Stadium");
        category.Name.Should().Be("Football");
    }

    [Fact]
    public async Task Should_StartTheCategoryActive()
    {
        // Act
        var category = await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - an inactive category accepts no new fixture, so one created inactive would look defined and
        // refuse every match opened against it.
        category.IsActive.Should().BeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static CreateCategoryCommand ACommand() => new("Football", "eleven a side", ["Stadium", "Arena"]);
}
