using System.Globalization;
using Microsoft.AspNetCore.Localization;
using StadiaPass.WebMVC.Authentication;
using StadiaPass.WebMVC.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeycloakLogin();

builder.Services.AddControllersWithViews();

builder.Services
    .AddHttpClient<IStadiaPassApiClient, StadiaPassApiClient>(client =>
        client.BaseAddress = new Uri("https+http://webapi"))
    .AddHttpMessageHandler<TokenBearerHandler>();

builder.Services
    .AddHttpClient<IStadiaPassCatalogueClient, StadiaPassCatalogueClient>(client =>
        client.BaseAddress = new Uri("https+http://webapi"))
    .AddHttpMessageHandler<TokenBearerHandler>();

builder.Services
    .AddHttpClient<IStadiaPassIdentityClient, StadiaPassIdentityClient>(client =>
        client.BaseAddress = new Uri("https+http://webapi"))
    .AddHttpMessageHandler<TokenBearerHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Model binding and Razor rendering must agree with jQuery unobtrusive validation, which parses numbers
// with an invariant decimal separator. Under a Turkish culture the server rendered "500,00" while the
// client validator read it as NaN, and a typed "500.50" bound as 50050.
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture),
    SupportedCultures = [CultureInfo.InvariantCulture],
    SupportedUICultures = [CultureInfo.InvariantCulture]
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapControllerRoute(name: "areas", pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(name: "default", pattern: "{controller=Matches}/{action=Index}/{id?}");

await app.RunAsync();
