using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.SharedKernel.Authorization;
using StadiaPass.WebMVC.Models;
using StadiaPass.WebMVC.Services;

namespace StadiaPass.WebMVC.Areas.Admin.Controllers;

/// <summary>
/// Back-office slice. The whole area is gated on the same permission the API enforces, so the form is never
/// even rendered for a customer - and the API refuses it anyway if someone crafts the request by hand.
/// </summary>
[Area("Admin")]
[Authorize(Policy = StadiaPassPermissions.Matches.Create)]
public sealed class MatchController(IStadiaPassApiClient apiClient, IStadiaPassCatalogueClient catalogue) : Controller
{

    /// <summary>
    /// The fixtures that can still be acted on. It is the customer listing behind this, deliberately: what
    /// that query returns - still ahead of us, not already called off - is exactly the set a cancellation
    /// applies to, so there is nothing here that could be offered and then refused.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await apiClient.GetMatchesAsync(cancellationToken: cancellationToken));

    /// <summary>
    /// The confirmation. A cancellation refunds every ticket sold for the fixture, so this is a page that
    /// says how many and asks why, rather than a browser dialog with a yes in it.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = StadiaPassPermissions.Matches.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var match = await FindCancellableAsync(id, cancellationToken);

        return match is null ? NotFound() : View(new CancelMatchInput { MatchId = id }.For(match));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Matches.Cancel)]
    public async Task<IActionResult> Cancel(CancelMatchInput input, CancellationToken cancellationToken)
    {
        var match = await FindCancellableAsync(input.MatchId, cancellationToken);

        if (match is null)
        {
            // Gone between opening the page and submitting it - somebody else called it off, or it kicked
            // off while the tab was open.
            TempData["Message"] = "That match can no longer be cancelled.";

            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            return View(input.For(match));
        }

        var result = await apiClient.CancelMatchAsync(input.MatchId, input.Reason, cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "The match could not be cancelled.");

            return View(input.For(match));
        }

        TempData["Message"] = match.SoldSeatCount is 0
            ? $"{match.HomeTeam} - {match.AwayTeam} was cancelled. No tickets had been sold."
            : $"{match.HomeTeam} - {match.AwayTeam} was cancelled. {match.SoldSeatCount} tickets are being "
              + "refunded; the money goes back on its own.";

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// The fixture, if it is one the listing still offers. Anything else - already cancelled, already played,
    /// never existed - is the same answer as far as this page is concerned.
    /// </summary>
    private async Task<MatchSummary?> FindCancellableAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var matches = await apiClient.GetMatchesAsync(cancellationToken: cancellationToken);

        return matches.FirstOrDefault(match => match.Id == matchId);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        await PopulateFormAsync(cancellationToken);

        return View(new CreateMatchInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateMatchInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await apiClient.CreateMatchAsync(input, cancellationToken);

            if (result.Succeeded)
            {
                TempData["Message"] = $"{result.Value!.HomeTeam} - {result.Value.AwayTeam} created with "
                                      + $"{result.Value.Capacity} seats.";

                return RedirectToAction("Index", "Matches", new { area = "" });
            }

            ApplyErrors(result);
        }

        await PopulateFormAsync(cancellationToken);

        return View(input);
    }

    private async Task PopulateFormAsync(CancellationToken cancellationToken)
    {
        ViewBag.Categories = await catalogue.GetCategoriesAsync(activeOnly: true, cancellationToken);
        ViewBag.Venues = await apiClient.GetVenuesAsync(cancellationToken);
    }

    private void ApplyErrors(ApiResult<MatchSummary> result)
    {
        if (result.ValidationErrors is null)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "The request could not be completed.");

            return;
        }

        foreach (var (property, messages) in result.ValidationErrors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(property, message);
            }
        }
    }
}
