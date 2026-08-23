using StadiaPass.Domain.Common;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Domain.Venues;

/// <summary>
/// A stadium or arena together with its seating plan. The plan is the template a match is opened against -
/// a match never invents seats of its own.
/// </summary>
public sealed class Venue : AggregateRoot
{
    public const int MaxCapacity = 25_000;

    private readonly List<VenueBlock> _blocks = [];

    private Venue()
    {
    }

    private Venue(Guid id, string name, string city, VenueKind kind)
        : base(id)
    {
        Name = name;
        City = city;
        Kind = kind;
    }

    public string Name { get; private set; } = null!;

    public string City { get; private set; } = null!;

    public VenueKind Kind { get; private set; }

    public IReadOnlyCollection<VenueBlock> Blocks => _blocks.AsReadOnly();

    public int Capacity => _blocks.Sum(block => block.Capacity);

    public static Venue Define(string name, string city, VenueKind kind, IEnumerable<BlockLayout> blocks)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
        {
            throw new DomainRuleViolationException(
                "Venue.InvalidName", "Venue name must be between 1 and 120 characters.");
        }

        if (string.IsNullOrWhiteSpace(city) || city.Length > 80)
        {
            throw new DomainRuleViolationException(
                "Venue.InvalidCity", "Venue city must be between 1 and 80 characters.");
        }

        var venue = new Venue(Guid.CreateVersion7(), name.Trim(), city.Trim(), kind);

        foreach (var block in blocks)
        {
            venue.AddBlock(block);
        }

        if (venue._blocks.Count is 0)
        {
            throw new DomainRuleViolationException(
                "Venue.NoBlocks", "A venue must define at least one seating block.");
        }

        return venue;
    }

    public void AddBlock(BlockLayout layout)
    {
        if (_blocks.Any(block => string.Equals(block.Name, layout.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainRuleViolationException(
                "Venue.DuplicateBlock", $"Block '{layout.Name}' is already defined for this venue.");
        }

        var candidate = VenueBlock.Create(layout.Name, layout.RowCount, layout.SeatsPerRow, layout.PriceMultiplier);

        if (Capacity + candidate.Capacity > MaxCapacity)
        {
            throw new DomainRuleViolationException(
                "Venue.CapacityExceeded",
                $"A venue seating plan is limited to {MaxCapacity} seats so a match can be materialised safely.");
        }

        _blocks.Add(candidate);
    }

    /// <summary>A football pitch is not played in a sports hall; the venue decides what it can host.</summary>
    public bool CanHost(SportCategory category) => category switch
    {
        SportCategory.Football => Kind is VenueKind.Stadium,
        SportCategory.Basketball or SportCategory.Volleyball or SportCategory.Handball =>
            Kind is VenueKind.Arena or VenueKind.Hall,
        _ => false
    };
}

public sealed record BlockLayout(string Name, int RowCount, int SeatsPerRow, decimal PriceMultiplier = 1m);
