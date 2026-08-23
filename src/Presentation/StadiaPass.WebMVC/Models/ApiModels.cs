using System.ComponentModel.DataAnnotations;

namespace StadiaPass.WebMVC.Models;

public sealed record MatchSummary(
    Guid Id,
    string HomeTeam,
    string AwayTeam,
    string Stadium,
    DateTimeOffset KickOffUtc,
    int Capacity,
    int IssuedTicketCount,
    int RemainingCapacity,
    string Status);

public sealed record TicketSummary(
    Guid Id,
    Guid MatchId,
    string SeatNumber,
    decimal Price,
    string Currency,
    string Status,
    string? HolderReference,
    DateTimeOffset? ReservationExpiresAtUtc,
    DateTimeOffset? SoldAtUtc);

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

public sealed class CreateTicketInput
{
    [Required]
    [Display(Name = "Match")]
    public Guid MatchId { get; set; }

    [Required]
    [StringLength(10)]
    [RegularExpression("^[A-Za-z0-9-]+$", ErrorMessage = "Block may only contain letters, digits and hyphens.")]
    [Display(Name = "Block")]
    public string Block { get; set; } = string.Empty;

    [Range(1, 500)]
    [Display(Name = "Row")]
    public int Row { get; set; } = 1;

    [Range(1, 500)]
    [Display(Name = "Seat")]
    public int Number { get; set; } = 1;

    [Range(0.01, 100_000)]
    [Display(Name = "Price")]
    public decimal Price { get; set; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    [Display(Name = "Currency")]
    public string Currency { get; set; } = "TRY";
}

public sealed class TicketBoardViewModel
{
    public required MatchSummary Match { get; init; }

    public required IReadOnlyList<TicketSummary> Tickets { get; init; }
}
