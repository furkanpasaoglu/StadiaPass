using NSubstitute;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Venues.Commands.CreateVenue;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.UnitTests.Venues;

/// <summary>
/// The seating plan every future match is materialised from. Two venues under the same name in the same city
/// is how a match ends up opened against the wrong plan, which nobody notices until the seat numbers on the
/// tickets do not match the building.
/// </summary>
public sealed class CreateVenueCommandHandlerTests
{
    private readonly IVenueRepository _venueRepository = Substitute.For<IVenueRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly CreateVenueCommandHandler _handler;

    public CreateVenueCommandHandlerTests() =>
        _handler = new CreateVenueCommandHandler(_venueRepository, _unitOfWork);

    [Fact]
    public async Task Should_Throw_When_TheSameVenueIsAlreadyDefinedInThatCity()
    {
        // Arrange
        _venueRepository
            .ExistsAsync("Sukru Saracoglu", "Istanbul", Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var creating = async () => await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - nothing may be written on the way out, or the duplicate exists anyway and only the response
        // says otherwise.
        await creating.Should().ThrowAsync<ConflictException>();
        await _venueRepository.DidNotReceive().AddAsync(Arg.Any<Venue>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_DefineTheVenueWithEveryBlockItWasGiven()
    {
        // Act
        var venue = await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - two rows of three plus three rows of four, counted by hand.
        venue.Capacity.Should().Be(18);
        venue.Blocks.Select(block => block.Name).Should().Equal("MARATON", "VIP");
        venue.Kind.Should().Be("Stadium");
    }

    [Fact]
    public async Task Should_CarryThePriceMultiplierOntoTheBlock()
    {
        // Act
        var venue = await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - the multiplier is what a match applies to its base price when it materialises seats, so a
        // dropped one sells the expensive seats at the cheap price.
        venue.Blocks.Single(block => block.Name == "VIP").PriceMultiplier.Should().Be(3m);
    }

    [Fact]
    public async Task Should_SaveOnce_When_TheVenueIsDefined()
    {
        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert
        await _venueRepository.Received(1).AddAsync(Arg.Any<Venue>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static CreateVenueCommand ACommand() => new(
        "Sukru Saracoglu",
        "Istanbul",
        "Stadium",
        [
            new VenueBlockInput("MARATON", RowCount: 2, SeatsPerRow: 3),
            new VenueBlockInput("VIP", RowCount: 3, SeatsPerRow: 4, PriceMultiplier: 3m)
        ]);
}
