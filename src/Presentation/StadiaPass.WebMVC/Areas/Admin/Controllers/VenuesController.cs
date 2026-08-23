using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.SharedKernel.Authorization;
using StadiaPass.WebMVC.Controllers;
using StadiaPass.WebMVC.Models;
using StadiaPass.WebMVC.Services;

namespace StadiaPass.WebMVC.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = StadiaPassPermissions.Venues.View)]
public sealed class VenuesController(IStadiaPassCatalogueClient catalogue) : Controller
{
    private static readonly string[] VenueKinds = ["Stadium", "Arena", "Hall"];

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new VenueListViewModel { Venues = await catalogue.GetVenuesAsync(cancellationToken) });

    [HttpGet]
    [Authorize(Policy = StadiaPassPermissions.Venues.Create)]
    public IActionResult Create()
    {
        ViewBag.VenueKinds = VenueKinds;

        return View("Edit", new VenueInput
        {
            Blocks = [new VenueBlockInputModel { Name = "A", RowCount = 10, SeatsPerRow = 15 }]
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Venues.Create)]
    public async Task<IActionResult> Create(VenueInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await catalogue.CreateVenueAsync(input, cancellationToken);

            if (result.Succeeded)
            {
                TempData["Message"] = $"{result.Value!.Name} created with {result.Value.Capacity} seats.";

                return RedirectToAction(nameof(Index));
            }

            this.ApplyApiErrors(result.Error, result.ValidationErrors);
        }

        ViewBag.VenueKinds = VenueKinds;

        return View("Edit", input);
    }

    [HttpGet]
    [Authorize(Policy = StadiaPassPermissions.Venues.Update)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var venue = (await catalogue.GetVenuesAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == id);

        if (venue is null)
        {
            return NotFound();
        }

        ViewBag.VenueKinds = VenueKinds;

        return View(new VenueInput
        {
            Id = venue.Id,
            Name = venue.Name,
            City = venue.City,
            Kind = venue.Kind,
            Blocks =
            [
                .. venue.Blocks.Select(block => new VenueBlockInputModel
                {
                    Name = block.Name,
                    RowCount = block.RowCount,
                    SeatsPerRow = block.SeatsPerRow,
                    PriceMultiplier = block.PriceMultiplier
                })
            ]
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Venues.Update)]
    public async Task<IActionResult> Edit(VenueInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await catalogue.UpdateVenueAsync(input, cancellationToken);

            if (result.Succeeded)
            {
                TempData["Message"] = $"{result.Value!.Name} updated.";

                return RedirectToAction(nameof(Index));
            }

            this.ApplyApiErrors(result.Error, result.ValidationErrors);
        }

        ViewBag.VenueKinds = VenueKinds;

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Venues.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await catalogue.DeleteVenueAsync(id, cancellationToken);

        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded ? "Venue deleted." : result.Error;

        return RedirectToAction(nameof(Index));
    }
}
