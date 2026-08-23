using System.ComponentModel.DataAnnotations;

namespace StadiaPass.WebMVC.Models;

public sealed record CategorySummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<string> AllowedVenueKinds);

public sealed class CategoryInput
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(60)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(255)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Can be played in")]
    public List<string> AllowedVenueKinds { get; set; } = [];
}

public sealed class VenueBlockInputModel
{
    [Required]
    [StringLength(10)]
    [RegularExpression("^[A-Za-z0-9-]+$", ErrorMessage = "Block name may only contain letters, digits and hyphens.")]
    [Display(Name = "Block")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 500)]
    [Display(Name = "Rows")]
    public int RowCount { get; set; } = 10;

    [Range(1, 500)]
    [Display(Name = "Seats per row")]
    public int SeatsPerRow { get; set; } = 15;

    [Range(0.01, 20)]
    [Display(Name = "Price multiplier")]
    public decimal PriceMultiplier { get; set; } = 1m;

    public int Capacity => RowCount * SeatsPerRow;
}

public sealed class VenueInput
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(120)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(80)]
    [Display(Name = "City")]
    public string City { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Kind")]
    public string Kind { get; set; } = "Stadium";

    /// <summary>Left empty on edit when the plan must stay as it is because matches already use it.</summary>
    public List<VenueBlockInputModel> Blocks { get; set; } = [];

    public bool CanEditBlocks { get; set; } = true;
}

public sealed class VenueListViewModel
{
    public required IReadOnlyList<VenueSummary> Venues { get; init; }
}

public sealed class CategoryListViewModel
{
    public required IReadOnlyList<CategorySummary> Categories { get; init; }
}
