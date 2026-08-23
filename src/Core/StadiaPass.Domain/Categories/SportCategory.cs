using StadiaPass.Domain.Common;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Domain.Categories;

/// <summary>
/// A sport a match can be opened for. This used to be an enum; it is an aggregate now so the back office can
/// add a discipline without a redeploy. The category also owns the rule about where it can be played, which
/// is where that knowledge belongs - a venue does not need to know the catalogue of sports.
/// </summary>
public sealed class SportCategory : AggregateRoot
{
    private readonly List<VenueKind> _allowedVenueKinds = [];

    private SportCategory()
    {
    }

    private SportCategory(Guid id, string name, string? description)
        : base(id)
    {
        Name = name;
        Description = description;
        IsActive = true;
    }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<VenueKind> AllowedVenueKinds => _allowedVenueKinds.AsReadOnly();

    public static SportCategory Define(string name, string? description, IEnumerable<VenueKind> allowedVenueKinds)
    {
        var category = new SportCategory(Guid.CreateVersion7(), NormaliseName(name), Trim(description));

        category.ChangeAllowedVenueKinds(allowedVenueKinds);

        return category;
    }

    public void Rename(string name, string? description)
    {
        Name = NormaliseName(name);
        Description = Trim(description);
    }

    public void ChangeAllowedVenueKinds(IEnumerable<VenueKind> allowedVenueKinds)
    {
        var kinds = allowedVenueKinds.Distinct().ToArray();

        if (kinds.Length is 0)
        {
            throw new DomainRuleViolationException(
                "Category.NoVenueKind", "A category must be playable in at least one kind of venue.");
        }

        _allowedVenueKinds.Clear();
        _allowedVenueKinds.AddRange(kinds);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public bool CanBePlayedIn(VenueKind venueKind) => _allowedVenueKinds.Contains(venueKind);

    public void EnsureCanBePlayedIn(Venue venue)
    {
        if (!IsActive)
        {
            throw new DomainRuleViolationException(
                "Category.Inactive", $"{Name} is not open for new matches.");
        }

        if (!CanBePlayedIn(venue.Kind))
        {
            throw new DomainRuleViolationException(
                "Category.VenueKindNotAllowed", $"{venue.Name} is a {venue.Kind} and cannot host {Name}.");
        }
    }

    private static string NormaliseName(string name) =>
        string.IsNullOrWhiteSpace(name) || name.Length > 60
            ? throw new DomainRuleViolationException(
                "Category.InvalidName", "Category name must be between 1 and 60 characters.")
            : name.Trim();

    private static string? Trim(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
