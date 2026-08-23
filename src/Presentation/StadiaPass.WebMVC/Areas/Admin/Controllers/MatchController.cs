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
public sealed class MatchController(IStadiaPassApiClient apiClient) : Controller
{
    private static readonly string[] Categories = ["Football", "Basketball", "Volleyball", "Handball"];

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
        ViewBag.Categories = Categories;
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
