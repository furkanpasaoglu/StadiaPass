using StadiaPass.Domain.Common;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Domain.UnitTests.Venues;

/// <summary>
/// The venue owns the seating plan a match is materialised from, so its invariants decide how large a match
/// can get and whether two blocks can collide.
/// </summary>
public sealed class VenueTests
{
    [Fact]
    public void Should_SumBlockCapacities_When_ThePlanIsDefined()
    {
        // Act
        var venue = TestData.Stadium(
            new BlockLayout("MARATON", RowCount: 10, SeatsPerRow: 15),
            new BlockLayout("VIP", RowCount: 4, SeatsPerRow: 10, PriceMultiplier: 3m));

        // Assert
        venue.Capacity.Should().Be(190);
        venue.Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void Should_ThrowDomainRuleViolation_When_TwoBlocksShareAName()
    {
        // Act - the comparison is case insensitive on purpose; "vip" and "VIP" are the same stand.
        var act = () => TestData.Stadium(
            new BlockLayout("VIP", RowCount: 1, SeatsPerRow: 1),
            new BlockLayout("vip", RowCount: 1, SeatsPerRow: 1));

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Venue.DuplicateBlock");
    }

    [Fact]
    public void Should_ThrowDomainRuleViolation_When_ThePlanExceedsTheMaximumCapacity()
    {
        // Arrange - the cap exists because a match materialises one row per seat.
        var oversized = new BlockLayout("A", RowCount: 500, SeatsPerRow: 500);

        // Act
        var act = () => TestData.Stadium(oversized);

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Venue.CapacityExceeded");
        Venue.MaxCapacity.Should().Be(25_000);
    }

    [Fact]
    public void Should_ThrowDomainRuleViolation_When_ThePlanHasNoBlocks()
    {
        // Act
        var act = () => Venue.Define("Empty", "Istanbul", VenueKind.Hall, []);

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Venue.NoBlocks");
    }

    [Fact]
    public void Should_ReplaceThePlan_When_BlocksAreReplaced()
    {
        // Arrange
        var venue = TestData.Stadium(new BlockLayout("MARATON", RowCount: 2, SeatsPerRow: 3));

        // Act
        venue.ReplaceBlocks([new BlockLayout("KUZEY", RowCount: 4, SeatsPerRow: 5)]);

        // Assert
        venue.Blocks.Should().ContainSingle().Which.Name.Should().Be("KUZEY");
        venue.Capacity.Should().Be(20);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_ThrowDomainRuleViolation_When_TheNameIsBlank(string name)
    {
        // Act
        var act = () => Venue.Define(
            name, "Istanbul", VenueKind.Hall, [new BlockLayout("A", RowCount: 1, SeatsPerRow: 1)]);

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Venue.InvalidName");
    }

    [Fact]
    public void Should_UppercaseBlockNames_When_ThePlanIsDefined()
    {
        // Act - seat numbers are rendered from the block name, so the casing is normalised once here.
        var venue = TestData.Stadium(new BlockLayout(" maraton ", RowCount: 1, SeatsPerRow: 1));

        // Assert
        venue.Blocks.Single().Name.Should().Be("MARATON");
    }
}
