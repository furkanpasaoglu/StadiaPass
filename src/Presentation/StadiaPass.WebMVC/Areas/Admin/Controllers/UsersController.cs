using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.SharedKernel.Authorization;
using StadiaPass.WebMVC.Models;
using StadiaPass.WebMVC.Services;

namespace StadiaPass.WebMVC.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = StadiaPassPermissions.Users.View)]
public sealed class UsersController(IStadiaPassIdentityClient identityClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        var list = await identityClient.GetUsersAsync(search, cancellationToken);

        return View(new UserPortalViewModel
        {
            Users = list.Users,
            AssignableRoles = list.AssignableRoles,
            Search = search
        });
    }

    [HttpGet]
    [Authorize(Policy = StadiaPassPermissions.Users.Create)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        ViewBag.AssignableRoles = (await identityClient.GetUsersAsync(null, cancellationToken)).AssignableRoles;

        return View(new CreateUserInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Users.Create)]
    public async Task<IActionResult> Create(CreateUserInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await identityClient.CreateUserAsync(input, cancellationToken);

            if (result.Succeeded)
            {
                TempData["Message"] = $"User {result.Value!.Username} created.";

                return RedirectToAction(nameof(Index));
            }

            ApplyErrors(result.Error, result.ValidationErrors);
        }

        ViewBag.AssignableRoles = (await identityClient.GetUsersAsync(null, cancellationToken)).AssignableRoles;

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Users.Manage)]
    public async Task<IActionResult> UpdateRole(string userId, string? role, CancellationToken cancellationToken)
    {
        string[] roles = string.IsNullOrWhiteSpace(role) ? [] : [role];

        var result = await identityClient.UpdateUserRolesAsync(userId, roles, cancellationToken);

        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded
            ? $"{result.Value!.Username} is now {(roles.Length is 0 ? "without a role" : role)}."
            : result.Error;

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Users.Manage)]
    public async Task<IActionResult> ToggleEnabled(
        string userId,
        string? email,
        string? firstName,
        string? lastName,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var result = await identityClient.UpdateUserAsync(
            userId, email, firstName, lastName, enabled, cancellationToken);

        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded
            ? $"{result.Value!.Username} is now {(enabled ? "enabled" : "disabled")}."
            : result.Error;

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Users.Manage)]
    public async Task<IActionResult> Delete(string userId, CancellationToken cancellationToken)
    {
        var result = await identityClient.DeleteUserAsync(userId, cancellationToken);

        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded ? "User deleted." : result.Error;

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
