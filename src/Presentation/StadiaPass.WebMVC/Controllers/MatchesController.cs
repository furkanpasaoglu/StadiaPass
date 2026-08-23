using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.SharedKernel.Authorization;
using StadiaPass.WebMVC.Models;
using StadiaPass.WebMVC.Services;

namespace StadiaPass.WebMVC.Controllers;

/// <summary>
/// Customer facing slice. Browsing is deliberately anonymous - a visitor sees the fixtures and the seat map
/// exactly like a real ticketing site - and only holding or buying a seat requires a sign-in.
/// </summary>
public sealed class MatchesController(IStadiaPassApiClient apiClient) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(string? category, CancellationToken cancellationToken)
    {
        var matches = await apiClient.GetMatchesAsync(category, cancellationToken);

        // The filter tabs come from what is actually on sale rather than a hard-coded list, so a category
        // added in the back office shows up here on its own.
        var categories = category is { Length: > 0 }
            ? await apiClient.GetMatchesAsync(null, cancellationToken)
            : matches;

        return View(new MatchListViewModel
        {
            Matches = matches,
            Categories = [.. categories.Select(match => match.Category).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            SelectedCategory = category
        });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> SeatSelection(
        Guid id,
        string? seat,
        CancellationToken cancellationToken)
    {
        var seatMap = await apiClient.GetSeatMapAsync(id, cancellationToken);

        if (seatMap is null)
        {
            return NotFound();
        }

        return View(new SeatSelectionViewModel
        {
            SeatMap = seatMap,
            SelectedSeatNumber = TempData["SelectedSeat"] as string,
            // Carried back from the sign-in round trip so the visitor lands on the seat they clicked.
            PendingSeatNumber = seat,
            ReservationExpiresAtUtc = TempData["ReservationExpiresAtUtc"] is string expiry
                ? DateTimeOffset.Parse(expiry, System.Globalization.CultureInfo.InvariantCulture)
                : null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Tickets.Reserve)]
    public async Task<IActionResult> Reserve(Guid id, string seatNumber, CancellationToken cancellationToken)
    {
        var result = await apiClient.ReserveSeatAsync(id, seatNumber, cancellationToken);

        if (result.Succeeded)
        {
            TempData["Message"] = $"Seat {seatNumber} is held for you until "
                                  + $"{result.Value!.ReservationExpiresAtUtc.ToLocalTime():HH:mm:ss}.";
            TempData["SelectedSeat"] = seatNumber;
            TempData["ReservationExpiresAtUtc"] = result.Value.ReservationExpiresAtUtc.ToString("O");
        }
        else
        {
            TempData["Error"] = result.Error;
        }

        return RedirectToAction(nameof(SeatSelection), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Tickets.Purchase)]
    public async Task<IActionResult> Purchase(Guid id, string seatNumber, CancellationToken cancellationToken)
    {
        var result = await apiClient.PurchaseAsync(id, seatNumber, cancellationToken);

        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            TempData["SelectedSeat"] = seatNumber;

            return RedirectToAction(nameof(SeatSelection), new { id });
        }

        TempData["Message"] = $"Ticket {result.Value!.AccessCode} issued for seat {result.Value.SeatNumber}.";

        return RedirectToAction("Index", "Tickets");
    }
}
