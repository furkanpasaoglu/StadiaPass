using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using StadiaPass.Application;
using StadiaPass.Infrastructure;
using StadiaPass.Persistence;
using StadiaPass.WebAPI.Authorization;
using StadiaPass.WebAPI.Endpoints;
using StadiaPass.WebAPI.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddPersistence();
builder.AddInfrastructure();
builder.Services.AddApplication();

builder.AddPermissionBasedAuthorization();

builder.Services.AddEndpoints(typeof(Program).Assembly);
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new()
        {
            Title = "StadiaPass API",
            Version = "v1",
            Description = "Stadium ticketing: match scheduling, ticket issuing, reservations and sales."
        };

        return Task.CompletedTask;
    });

    options.AddDocumentTransformer<OAuth2SecuritySchemeTransformer>();
    options.AddOperationTransformer<PermissionOperationTransformer>();
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    var keycloak = app.Services.GetRequiredService<IOptions<KeycloakOptions>>().Value;

    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("StadiaPass API")
        .WithTheme(ScalarTheme.BluePlanet)
        .AddPreferredSecuritySchemes(OAuth2SecuritySchemeTransformer.SchemeId)
        .AddAuthorizationCodeFlow(OAuth2SecuritySchemeTransformer.SchemeId, flow =>
        {
            flow.ClientId = keycloak.ScalarClientId;
            flow.Pkce = Pkce.Sha256;
            flow.SelectedScopes = ["openid", "profile"];
        }));
}

app.MapDefaultEndpoints();
app.MapEndpoints();

await app.RunAsync();

public partial class Program;
