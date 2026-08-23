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
    public IActionResult Logout() =>
        SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);

    [HttpGet]
    public IActionResult Denied() => View();

    private string LocalOrHome(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}
