using StadiaPass.WebMVC.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient<IStadiaPassApiClient, StadiaPassApiClient>(client =>
    client.BaseAddress = new Uri("https+http://webapi"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapControllerRoute(name: "default", pattern: "{controller=Tickets}/{action=Index}/{id?}");

await app.RunAsync();
