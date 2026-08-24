using System.ComponentModel.DataAnnotations;

namespace StadiaPass.WebMVC.Models;

public sealed record MatchSummary(
    Guid Id,
    Guid CategoryId,
    string Category,
    Guid VenueId,
    string VenueName,
    string HomeTeam,
    string AwayTeam,
    DateTimeOffset KickOffUtc,
    string Status,
    int Capacity,
    int AvailableSeatCount,
    int ReservedSeatCount,
    int SoldSeatCount);

public sealed record SeatMap(
    Guid MatchId,
    string Category,
    string VenueName,
    string HomeTeam,
    string AwayTeam,
    DateTimeOffset KickOffUtc,
    string Status,
    int Capacity,
    int AvailableSeatCount,
    IReadOnlyList<SeatBlock> Blocks);

public sealed record SeatBlock(string Block, int AvailableSeatCount, IReadOnlyList<SeatRow> Rows);

public sealed record SeatRow(int Row, IReadOnlyList<Seat> Seats);

public sealed record Seat(string SeatNumber, int Number, decimal Price, string Currency, string Status);

public sealed record SeatReservation(
    Guid MatchId,
    string SeatNumber,
    decimal Price,
    string Currency,
    string Status,
    DateTimeOffset ReservationExpiresAtUtc);

public sealed record TicketSummary(
    Guid Id,
    Guid MatchId,
    Guid MatchSeatId,
    string SeatNumber,
    decimal Price,
    string Currency,
    string HolderReference,
    string AccessCode,
    DateTimeOffset IssuedAtUtc,
    string Status);

public sealed record VenueSummary(
    Guid Id,
    string Name,
    string City,
    string Kind,
    int Capacity,
    IReadOnlyList<VenueBlockSummary> Blocks);

public sealed record VenueBlockSummary(string Name, int RowCount, int SeatsPerRow, decimal PriceMultiplier, int Capacity);

public sealed record ApiResult<T>(
    bool Succeeded,
    T? Value,
    string? Error = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public static class ApiResult
{
    public static ApiResult<T> Success<T>(T value) => new(true, value);

    public static ApiResult<T> Failure<T>(
        string error,
        IReadOnlyDictionary<string, string[]>? validationErrors = null) =>
        new(false, default, error, validationErrors);
}

public sealed class CreateMatchInput
{
    [Required]
    [Display(Name = "Sport category")]
    public Guid CategoryId { get; set; }

    [Required]
    [Display(Name = "Venue")]
    public Guid VenueId { get; set; }

    [Required]
    [StringLength(80)]
    [Display(Name = "Home team")]
    public string HomeTeam { get; set; } = string.Empty;

    [Required]
    [StringLength(80)]
    [Display(Name = "Away team")]
    public string AwayTeam { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Kick-off (local time)")]
    [DataType(DataType.DateTime)]
    // datetime-local expects exactly this shape; the default round-trip format adds milliseconds that some
    // browsers refuse to pre-fill.
    [DisplayFormat(DataFormatString = "{0:yyyy-MM-ddTHH:mm}", ApplyFormatInEditMode = true)]
    public DateTime KickOffLocal { get; set; } = DateTime.Now.Date.AddDays(7).AddHours(20);

    [Required]
    [Range(0.01, 100_000)]
    [Display(Name = "Base price")]
    public decimal BasePrice { get; set; } = 500m;

    [Required]
    [StringLength(3, MinimumLength = 3)]
    [Display(Name = "Currency")]
    public string Currency { get; set; } = "TRY";
}

/// <summary>
/// What the checkout panel posts. These values are relayed to the API for a single charge and are never
/// written anywhere: not to TempData, not to the model state that survives a redirect, and not to the log -
/// the number and the security code are masked by name before any log event is written.
/// </summary>
public sealed class PurchaseInput
{
    [Required]
    public string SeatNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "The name printed on the card is required.")]
    [StringLength(128)]
    [Display(Name = "Name on card")]
    public string CardHolderName { get; set; } = string.Empty;

    [Required(ErrorMessage = "A card number is required.")]
    [StringLength(23, MinimumLength = 13)]
    [Display(Name = "Card number")]
    public string CardNumber { get; set; } = string.Empty;

    [Range(1, 12)]
    [Display(Name = "Month")]
    public int ExpirationMonth { get; set; } = DateTime.Now.Month;

    [Range(2000, 2100)]
    [Display(Name = "Year")]
    public int ExpirationYear { get; set; } = DateTime.Now.Year + 1;

    [Required(ErrorMessage = "The security code is required.")]
    [RegularExpression("^[0-9]{3,4}$", ErrorMessage = "The security code is three or four digits.")]
    [Display(Name = "CVV")]
    public string Cvv { get; set; } = string.Empty;
}

public sealed class SeatSelectionViewModel
{
    public required SeatMap SeatMap { get; init; }

    /// <summary>Seat currently held for the signed-in visitor.</summary>
    public string? SelectedSeatNumber { get; init; }

    /// <summary>Seat an anonymous visitor clicked before being sent through the sign-in round trip.</summary>
    public string? PendingSeatNumber { get; init; }

    public DateTimeOffset? ReservationExpiresAtUtc { get; init; }
}

public sealed class MatchListViewModel
{
    public required IReadOnlyList<MatchSummary> Matches { get; init; }

    public required IReadOnlyList<string> Categories { get; init; }

    public string? SelectedCategory { get; init; }
}
