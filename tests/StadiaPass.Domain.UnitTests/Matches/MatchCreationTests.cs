using StadiaPass.Domain.Common;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Domain.UnitTests.Matches;

/// <summary>
/// Opening a match is where the catalogue rules bite: the venue has to be able to host the sport, and the
/// seat map has to be materialised from the venue plan rather than invented by the match.
/// </summary>
public sealed class MatchCreationTests
{
    [Fact]
    public void Should_ThrowDomainRuleViolation_When_VenueKindCannotHostTheCategory()
    {
        // Arrange - football is declared playable in a stadium only.
        var football = TestData.Football();
        var arena = TestData.Arena();

        // Act
        var act = () => Match.Create(
            football, arena, "Fenerbahce", "Galatasaray", TestData.KickOff, Money.Create(100m), TestData.Now);

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Category.VenueKindNotAllowed")
            .WithMessage("*Arena*cannot host Football*");
    }

    [Fact]
    public void Should_CreateMatch_When_VenueKindCanHostTheCategory()
    {
        // Arrange
        var basketball = TestData.Basketball();
        var arena = TestData.Arena();

        // Act
        var match = Match.Create(
            basketball, arena, "Anadolu Efes", "Fenerbahce Beko", TestData.KickOff, Money.Create(100m), TestData.Now);

        // Assert
        match.CategoryName.Should().Be("Basketball");
        match.VenueId.Should().Be(arena.Id);
        match.Status.Should().Be(MatchStatus.OnSale);
    }

    [Fact]
    public void Should_ThrowDomainRuleViolation_When_CategoryIsInactive()
    {
        // Arrange - a retired sport must not accept new fixtures.
        var football = TestData.Football();
        football.Deactivate();

        // Act
        var act = () => Match.Create(
            football, TestData.Stadium(), "Fenerbahce", "Galatasaray",
            TestData.KickOff, Money.Create(100m), TestData.Now);

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Category.Inactive");
    }

    [Fact]
    public void Should_MaterialiseEverySeatOfTheVenuePlan_When_MatchIsCreated()
    {
        // Arrange - two blocks: 2x3 at face value and 1x2 at triple.
        var venue = TestData.Stadium(
            new BlockLayout("MARATON", RowCount: 2, SeatsPerRow: 3),
            new BlockLayout("VIP", RowCount: 1, SeatsPerRow: 2, PriceMultiplier: 3m));

        // Act
        var match = TestData.FootballMatch(venue, basePrice: 100m);

        // Assert
        match.Capacity.Should().Be(8);
        match.AvailableSeatCount.Should().Be(8);
        match.SoldSeatCount.Should().Be(0);
        match.Seats.Should().OnlyContain(seat => seat.Status == SeatStatus.Available);
        match.Seats.Select(seat => seat.SeatNumber.ToString())
            .Should().Contain(["MARATON-1-1", "MARATON-2-3", "VIP-1-2"]);
    }

    [Fact]
    public void Should_ApplyTheBlockMultiplierToEverySeatPrice_When_MatchIsCreated()
    {
        // Arrange
        var venue = TestData.Stadium(
            new BlockLayout("MARATON", RowCount: 1, SeatsPerRow: 1),
            new BlockLayout("KALE", RowCount: 1, SeatsPerRow: 1, PriceMultiplier: 0.75m),
            new BlockLayout("VIP", RowCount: 1, SeatsPerRow: 1, PriceMultiplier: 3m));

        // Act
        var match = TestData.FootballMatch(venue, basePrice: 1200m);

        // Assert
        TestData.SeatOf(match, "MARATON-1-1").Price.Amount.Should().Be(1200m);
        TestData.SeatOf(match, "KALE-1-1").Price.Amount.Should().Be(900m);
        TestData.SeatOf(match, "VIP-1-1").Price.Amount.Should().Be(3600m);
    }

    [Fact]
    public void Should_NormaliseKickOffToUtc_When_MatchIsCreated()
    {
        // Arrange - the column is timestamptz, which only accepts a zero offset.
        var localKickOff = new DateTimeOffset(2026, 9, 1, 20, 0, 0, TimeSpan.FromHours(3));

        // Act
        var match = Match.Create(
            TestData.Football(), TestData.Stadium(), "Fenerbahce", "Galatasaray",
            localKickOff, Money.Create(100m), TestData.Now);

        // Assert
        match.KickOffUtc.Offset.Should().Be(TimeSpan.Zero);
        match.KickOffUtc.Hour.Should().Be(17);
    }

    [Theory]
    [InlineData("Fenerbahce", "fenerbahce")]
    [InlineData("Fenerbahce", "FENERBAHCE")]
    public void Should_ThrowDomainRuleViolation_When_ATeamPlaysItself(string home, string away)
    {
        // Act
        var act = () => Match.Create(
            TestData.Football(), TestData.Stadium(), home, away,
            TestData.KickOff, Money.Create(100m), TestData.Now);

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Match.SameTeam");
    }

    [Fact]
    public void Should_ThrowDomainRuleViolation_When_KickOffIsNotInTheFuture()
    {
        // Act
        var act = () => Match.Create(
            TestData.Football(), TestData.Stadium(), "Fenerbahce", "Galatasaray",
            TestData.Now.AddSeconds(-1), Money.Create(100m), TestData.Now);

        // Assert
        act.Should().Throw<DomainRuleViolationException>()
            .Where(exception => exception.Rule == "Match.KickOffInPast");
    }
}
