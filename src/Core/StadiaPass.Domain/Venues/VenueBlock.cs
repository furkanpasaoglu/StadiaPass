using StadiaPass.Domain.Common;

namespace StadiaPass.Domain.Venues;

/// <summary>A named seating block of a venue, e.g. "MARATON" with 12 rows of 20 seats.</summary>
public sealed class VenueBlock : Entity
{
    private VenueBlock()
    {
    }

    private VenueBlock(Guid id, string name, int rowCount, int seatsPerRow, decimal priceMultiplier)
        : base(id)
    {
        Name = name;
        RowCount = rowCount;
        SeatsPerRow = seatsPerRow;
        PriceMultiplier = priceMultiplier;
    }

    public string Name { get; private set; } = null!;

    public int RowCount { get; private set; }

    public int SeatsPerRow { get; private set; }

    /// <summary>Applied to the match base price, so a VIP block can cost more than the terraces.</summary>
    public decimal PriceMultiplier { get; private set; }

    public int Capacity => RowCount * SeatsPerRow;

    internal static VenueBlock Create(string name, int rowCount, int seatsPerRow, decimal priceMultiplier)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 10)
        {
            throw new DomainRuleViolationException(
                "VenueBlock.InvalidName", "Block name must be between 1 and 10 characters.");
        }

        if (rowCount is <= 0 or > 500)
        {
            throw new DomainRuleViolationException(
                "VenueBlock.InvalidRowCount", "A block must have between 1 and 500 rows.");
        }

        if (seatsPerRow is <= 0 or > 500)
        {
            throw new DomainRuleViolationException(
                "VenueBlock.InvalidSeatsPerRow", "A block row must have between 1 and 500 seats.");
        }

        if (priceMultiplier is <= 0m or > 20m)
        {
            throw new DomainRuleViolationException(
                "VenueBlock.InvalidPriceMultiplier", "Price multiplier must be greater than 0 and at most 20.");
        }

        return new VenueBlock(
            Guid.CreateVersion7(),
            name.Trim().ToUpperInvariant(),
            rowCount,
            seatsPerRow,
            decimal.Round(priceMultiplier, 2, MidpointRounding.ToEven));
    }
}
