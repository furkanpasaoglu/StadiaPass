using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Serilog;
using StadiaPass.ServiceDefaults.Logging;
using StadiaPass.WebMVC.Authentication;
using StadiaPass.WebMVC.Services;

// A bootstrap logger covers the window before the host exists: a misconfigured Keycloak authority would
// otherwise take the process down without a single line explaining why. AddServiceDefaults swaps it for the
// fully configured logger as soon as the container is built.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
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

    // After the static file handler on purpose: stylesheets and scripts are served without ever reaching
    // this point, so a single page view stays a single log line instead of a dozen.
    app.UseStadiaPassRequestLogging();

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapDefaultEndpoints();
    app.MapControllerRoute(name: "areas", pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
    app.MapControllerRoute(name: "default", pattern: "{controller=Matches}/{action=Index}/{id?}");

    await app.RunAsync();
}
catch (Exception exception)
{
    Log.Fatal(exception, "StadiaPass web terminated unexpectedly");

    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
