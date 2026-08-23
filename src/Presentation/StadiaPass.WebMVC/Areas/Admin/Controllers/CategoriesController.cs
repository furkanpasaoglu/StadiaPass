using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.SharedKernel.Authorization;
using StadiaPass.WebMVC.Controllers;
using StadiaPass.WebMVC.Models;
using StadiaPass.WebMVC.Services;

namespace StadiaPass.WebMVC.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = StadiaPassPermissions.Categories.View)]
public sealed class CategoriesController(IStadiaPassCatalogueClient catalogue) : Controller
{
    private static readonly string[] VenueKinds = ["Stadium", "Arena", "Hall"];

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new CategoryListViewModel { Categories = await catalogue.GetCategoriesAsync(false, cancellationToken) });

    [HttpGet]
    [Authorize(Policy = StadiaPassPermissions.Categories.Create)]
    public IActionResult Create()
    {
        ViewBag.VenueKinds = VenueKinds;

        return View("Edit", new CategoryInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Categories.Create)]
    public async Task<IActionResult> Create(CategoryInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await catalogue.CreateCategoryAsync(input, cancellationToken);

            if (result.Succeeded)
            {
                TempData["Message"] = $"Category {result.Value!.Name} created.";

                return RedirectToAction(nameof(Index));
            }

            this.ApplyApiErrors(result.Error, result.ValidationErrors);
        }

        ViewBag.VenueKinds = VenueKinds;

        return View("Edit", input);
    }

    [HttpGet]
    [Authorize(Policy = StadiaPassPermissions.Categories.Update)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var categories = await catalogue.GetCategoriesAsync(false, cancellationToken);
        var category = categories.FirstOrDefault(candidate => candidate.Id == id);

        if (category is null)
        {
            return NotFound();
        }

        ViewBag.VenueKinds = VenueKinds;

        return View(new CategoryInput
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            IsActive = category.IsActive,
            AllowedVenueKinds = [.. category.AllowedVenueKinds]
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Categories.Update)]
    public async Task<IActionResult> Edit(CategoryInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await catalogue.UpdateCategoryAsync(input, cancellationToken);

            if (result.Succeeded)
            {
                TempData["Message"] = $"Category {result.Value!.Name} updated.";

                return RedirectToAction(nameof(Index));
            }

            this.ApplyApiErrors(result.Error, result.ValidationErrors);
        }

        ViewBag.VenueKinds = VenueKinds;

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Categories.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await catalogue.DeleteCategoryAsync(id, cancellationToken);

        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded ? "Category deleted." : result.Error;

        return RedirectToAction(nameof(Index));
    }
}
