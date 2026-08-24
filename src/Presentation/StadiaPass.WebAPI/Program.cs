using System.Globalization;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using StadiaPass.Application;
using StadiaPass.Infrastructure;
using StadiaPass.Persistence;
using StadiaPass.ServiceDefaults.Configuration;
using StadiaPass.ServiceDefaults.Logging;
using StadiaPass.WebAPI.Authorization;
using StadiaPass.WebAPI.Endpoints;
using StadiaPass.WebAPI.Extensions;

// A bootstrap logger covers the window before the host exists: a bad connection string or a malformed
// realm would otherwise take the process down without a single line explaining why. AddServiceDefaults
// swaps it for the fully configured logger as soon as the container is built.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Vault first: a connection string is read while the container is being built, so a configuration
    // source added any later would arrive after the thing that needed it.
    builder.AddVaultConfiguration();

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

    // Outermost, so the single summary line carries the status code the caller actually received - the one
    // the exception handler below turned a failure into.
    app.UseStadiaPassRequestLogging();

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
}
catch (Exception exception)
{
    Log.Fatal(exception, "StadiaPass API terminated unexpectedly");

    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
