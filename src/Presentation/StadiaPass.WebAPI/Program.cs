using Scalar.AspNetCore;
using StadiaPass.Application;
using StadiaPass.Infrastructure;
using StadiaPass.Persistence;
using StadiaPass.WebAPI.Endpoints;
using StadiaPass.WebAPI.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddPersistence();
builder.AddInfrastructure();
builder.Services.AddApplication();

builder.Services.AddEndpoints(typeof(Program).Assembly);
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new()
    {
        Title = "StadiaPass API",
        Version = "v1",
        Description = "Stadium ticketing: match scheduling, ticket issuing, reservations and sales."
    };

    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("StadiaPass API")
        .WithTheme(ScalarTheme.BluePlanet));
}

app.MapDefaultEndpoints();
app.MapEndpoints();

await app.RunAsync();

public partial class Program;
