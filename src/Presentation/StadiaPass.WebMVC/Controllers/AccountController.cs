using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace StadiaPass.WebMVC.Controllers;

public sealed class AccountController : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null) =>
        Challenge(
            new AuthenticationProperties { RedirectUri = LocalOrHome(returnUrl) },
            OpenIdConnectDefaults.AuthenticationScheme);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        // A stale tab can post a sign-out for a session that is already gone. Driving the OIDC sign-out
        // without an id token only earns an error page from Keycloak, so go straight home instead.
        if (User.Identity?.IsAuthenticated is not true)
        {
            return RedirectToAction("Index", "Matches");
        }

        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Sign-out is a POST so it cannot be triggered by a link. A stray GET - a bookmark, the back button, a
    /// hand-typed URL - would otherwise be answered with a bare 405, so send the caller home.
    /// </summary>
    [HttpGet]
    [ActionName(nameof(Logout))]
    public IActionResult LogoutFallback() => RedirectToAction("Index", "Matches");

    [HttpGet]
    public IActionResult Denied() => View();

    private string LocalOrHome(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}
