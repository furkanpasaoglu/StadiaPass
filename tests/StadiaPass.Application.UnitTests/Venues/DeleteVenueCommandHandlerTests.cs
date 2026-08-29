using NSubstitute;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Venues.Commands.DeleteVenue;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.UnitTests.Venues;

/// <summary>
/// A match holds the venue by identifier and denormalises only its name, so deleting a venue underneath one
/// does not fail loudly - it leaves a fixture pointing at nothing, and the failure surfaces much later at a
/// reindex or a seat map.
/// </summary>
public sealed class DeleteVenueCommandHandlerTests
{
    private readonly IVenueRepository _venueRepository = Substitute.For<IVenueRepository>();

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly DeleteVenueCommandHandler _handler;

    private readonly Venue _venue = TestData.Stadium();

    public DeleteVenueCommandHandlerTests()
    {
        _venueRepository
            .GetTrackedWithBlocksAsync(_venue.Id, Arg.Any<CancellationToken>())
            .Returns(_venue);

        _handler = new DeleteVenueCommandHandler(_venueRepository, _matchRepository, _unitOfWork);
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheVenueIsAlreadyGone()
    {
        // Arrange
        _venueRepository
            .GetTrackedWithBlocksAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Venue?)null);

        // Act
        var deleting = async () => await _handler.Handle(new DeleteVenueCommand(_venue.Id), CancellationToken.None);

        // Assert
        await deleting.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_Refuse_When_AtLeastOneMatchUsesTheVenue()
    {
        // Arrange
        _matchRepository.ExistsForVenueAsync(_venue.Id, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var deleting = async () => await _handler.Handle(new DeleteVenueCommand(_venue.Id), CancellationToken.None);

        // Assert - the database would either refuse this on a foreign key, which surfaces as a 500, or accept
        // it and leave the fixture orphaned. Neither tells the administrator what actually stopped them.
        await deleting.Should().ThrowAsync<ConflictException>();
        _venueRepository.DidNotReceive().Remove(Arg.Any<Venue>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RemoveTheVenue_When_NoMatchUsesIt()
    {
        // Arrange
        _matchRepository.ExistsForVenueAsync(_venue.Id, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        await _handler.Handle(new DeleteVenueCommand(_venue.Id), CancellationToken.None);

        // Assert
        _venueRepository.Received(1).Remove(_venue);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
