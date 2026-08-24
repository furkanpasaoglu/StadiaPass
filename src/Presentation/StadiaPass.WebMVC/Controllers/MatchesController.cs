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

    /// <summary>
    /// The card is read off the form, handed to the API and forgotten. Only the seat number goes into
    /// TempData on failure - carrying card details through a redirect would put them in a cookie.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Tickets.Purchase)]
    public async Task<IActionResult> Purchase(Guid id, PurchaseInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Check the card details and try again.";
            TempData["SelectedSeat"] = input.SeatNumber;

            return RedirectToAction(nameof(SeatSelection), new { id });
        }

        var result = await apiClient.PurchaseAsync(id, input, cancellationToken);

        if (!result.Succeeded)
        {
            // A decline is not a dead end: the seat is still held, so the customer lands back on the seat
            // map with the panel open and can try another card while the hold lasts.
            TempData["Error"] = result.Error;
            TempData["SelectedSeat"] = input.SeatNumber;

            return RedirectToAction(nameof(SeatSelection), new { id });
        }

        TempData["Message"] = $"Ticket {result.Value!.AccessCode} issued for seat {result.Value.SeatNumber}.";

        return RedirectToAction("Index", "Tickets");
    }
}
