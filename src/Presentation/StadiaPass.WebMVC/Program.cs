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
    .AddHttpClient<IStadiaPassIdentityClient, StadiaPassIdentityClient>(client =>
        client.BaseAddress = new Uri("https+http://webapi"))
    .AddHttpMessageHandler<TokenBearerHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapControllerRoute(name: "areas", pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(name: "default", pattern: "{controller=Matches}/{action=Index}/{id?}");

await app.RunAsync();
