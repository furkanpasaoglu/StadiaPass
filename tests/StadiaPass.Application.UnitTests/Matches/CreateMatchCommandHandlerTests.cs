using NSubstitute;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Matches.Commands.CreateMatch;
using StadiaPass.Application.Matches.Events;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Categories;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.UnitTests.Matches;

/// <summary>
/// Opening a fixture is the one write that has to leave three things consistent at once: the seats the venue
/// plan implies, the message that puts the fixture in front of anyone searching, and the cached listing that
/// would otherwise keep saying the fixture does not exist.
/// </summary>
public sealed class CreateMatchCommandHandlerTests
{
    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IVenueRepository _venueRepository = Substitute.For<IVenueRepository>();

    private readonly ISportCategoryRepository _categoryRepository = Substitute.For<ISportCategoryRepository>();

    private readonly ICacheService _cacheService = Substitute.For<ICacheService>();

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    private readonly CreateMatchCommandHandler _handler;

    private readonly Venue _venue = TestData.Stadium();

    private readonly SportCategory _category = TestData.Football();

    public CreateMatchCommandHandlerTests()
    {
        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        _venueRepository.GetWithBlocksAsync(_venue.Id, Arg.Any<CancellationToken>()).Returns(_venue);
        _categoryRepository.GetByIdAsync(_category.Id, Arg.Any<CancellationToken>()).Returns(_category);

        _handler = new CreateMatchCommandHandler(
            _matchRepository,
            _venueRepository,
            _categoryRepository,
            _cacheService,
            _outbox,
            _unitOfWork,
            _dateTimeProvider);
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheVenueIsNotThere()
    {
        // Arrange
        _venueRepository
            .GetWithBlocksAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Venue?)null);

        // Act
        var creating = async () => await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - without this the aggregate is handed a null plan and the failure surfaces as a 500 rather
        // than telling the caller which identifier was wrong.
        await creating.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheCategoryIsNotThere()
    {
        // Arrange
        _categoryRepository
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((SportCategory?)null);

        // Act
        var creating = async () => await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert
        await creating.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_MaterialiseASeatForEveryPlaceInThePlan_When_TheMatchIsOpened()
    {
        // Act
        var match = await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - one block of two rows by three: six seats, all of them free and none of them sold.
        match.Capacity.Should().Be(6);
        match.AvailableSeatCount.Should().Be(6);
        match.ReservedSeatCount.Should().Be(0);
        match.SoldSeatCount.Should().Be(0);
        match.Status.Should().Be("OnSale");
        await _matchRepository.Received(1).AddAsync(Arg.Any<Match>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_StageTheSearchIndexMessageBeforeTheSave_When_TheMatchIsOpened()
    {
        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - the outbox row and the fixture have to be written by the same SaveChanges. Enqueuing
        // afterwards would let the two disagree: a fixture nobody can find, or a message about a fixture that
        // rolled back.
        Received.InOrder(() =>
        {
            _outbox.Enqueue(Arg.Any<MatchCatalogueChangedEvent>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Should_NameTheMatchItJustOpened_When_TheIndexMessageIsStaged()
    {
        // Arrange
        MatchCatalogueChangedEvent? staged = null;
        _outbox.Enqueue(Arg.Do<object>(message => staged = message as MatchCatalogueChangedEvent));

        // Act
        var match = await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - a message carrying the wrong identifier indexes nothing and nobody notices, because the
        // projection succeeds either way.
        staged.Should().NotBeNull();
        staged!.MatchId.Should().Be(match.Id);
    }

    [Fact]
    public async Task Should_DropTheUpcomingListingFromTheCache_When_TheMatchIsOpened()
    {
        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - the listing is cached for fifteen seconds, so a fixture opened and not invalidated is a
        // fixture the front page denies exists. The key is spelled out here on purpose: the listing is held
        // under one key and filtered in memory, and a per-category key was what once made the tabs disagree.
        await _cacheService.Received(1).RemoveAsync("matches:upcoming", Arg.Any<CancellationToken>());
    }

    private CreateMatchCommand ACommand() => new(
        _category.Id,
        _venue.Id,
        "Fenerbahce",
        "Galatasaray",
        TestData.Now.AddDays(30),
        BasePrice: 100m);
}
