using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.SharedKernel.Authorization;
using StadiaPass.WebMVC.Models;
using StadiaPass.WebMVC.Services;

namespace StadiaPass.WebMVC.Controllers;

/// <summary>Customer facing slice: browse matches by sport category, then pick a seat off the map.</summary>
[Authorize]
public sealed class MatchesController(IStadiaPassApiClient apiClient) : Controller
{
    private static readonly string[] Categories = ["Football", "Basketball", "Volleyball", "Handball"];

    [HttpGet]
    [Authorize(Policy = StadiaPassPermissions.Matches.View)]
    public async Task<IActionResult> Index(string? category, CancellationToken cancellationToken)
    {
        var matches = await apiClient.GetMatchesAsync(category, cancellationToken);

        return View(new MatchListViewModel
        {
            Matches = matches,
            Categories = Categories,
            SelectedCategory = category
        });
    }

    [HttpGet]
    [Authorize(Policy = StadiaPassPermissions.Matches.View)]
    public async Task<IActionResult> SeatSelection(Guid id, string? selected, CancellationToken cancellationToken)
    {
        var seatMap = await apiClient.GetSeatMapAsync(id, cancellationToken);

        if (seatMap is null)
        {
            return NotFound();
        }

        return View(new SeatSelectionViewModel
        {
            SeatMap = seatMap,
            SelectedSeatNumber = selected ?? TempData["SelectedSeat"] as string,
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
