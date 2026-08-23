using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.SharedKernel.Authorization;
using StadiaPass.WebMVC.Models;
using StadiaPass.WebMVC.Services;

namespace StadiaPass.WebMVC.Areas.Admin.Controllers;

/// <summary>
/// Roles and permissions portal. The checklist is rendered straight from the permission catalogue the API
/// publishes, so a permission added to <see cref="StadiaPassPermissions"/> shows up here with no UI change.
/// </summary>
[Area("Admin")]
[Authorize(Policy = StadiaPassPermissions.Roles.Manage)]
public sealed class RolesController(IStadiaPassIdentityClient identityClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? role, CancellationToken cancellationToken)
    {
        var list = await identityClient.GetRolesAsync(cancellationToken);

        return View(new RoleEditorViewModel
        {
            Roles = list.Roles,
            PermissionCatalogue = list.PermissionCatalogue,
            Selected = role is { Length: > 0 }
                ? list.Roles.FirstOrDefault(candidate => string.Equals(candidate.Name, role, StringComparison.Ordinal))
                : null
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var list = await identityClient.GetRolesAsync(cancellationToken);

        ViewBag.PermissionCatalogue = list.PermissionCatalogue;

        return View(new CreateRoleInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateRoleInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await identityClient.CreateRoleAsync(input, cancellationToken);

            if (result.Succeeded)
            {
                TempData["Message"] = $"Role {result.Value!.Name} created with {result.Value.Permissions.Count} permissions.";

                return RedirectToAction(nameof(Index), new { role = result.Value.Name });
            }

            ApplyErrors(result.Error, result.ValidationErrors);
        }

        ViewBag.PermissionCatalogue = (await identityClient.GetRolesAsync(cancellationToken)).PermissionCatalogue;

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePermissions(RolePermissionsInput input, CancellationToken cancellationToken)
    {
        var result = await identityClient.UpdateRolePermissionsAsync(
            input.RoleName, input.Permissions, cancellationToken);

        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded
            ? $"{input.RoleName} now grants {result.Value!.Permissions.Count} permissions."
            : result.Error;

        return RedirectToAction(nameof(Index), new { role = input.RoleName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string roleName, CancellationToken cancellationToken)
    {
        var result = await identityClient.DeleteRoleAsync(roleName, cancellationToken);

        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded
            ? $"Role {roleName} deleted."
            : result.Error;

        return RedirectToAction(nameof(Index));
    }

    private void ApplyErrors(string? error, IReadOnlyDictionary<string, string[]>? validationErrors)
    {
        if (validationErrors is null)
        {
            ModelState.AddModelError(string.Empty, error ?? "The request could not be completed.");

            return;
        }

        foreach (var (property, messages) in validationErrors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(property, message);
            }
        }
    }
}
