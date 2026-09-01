using System.Globalization;
using Serilog;
using StadiaPass.McpServer.Api;
using StadiaPass.McpServer.Tools;
using StadiaPass.ServiceDefaults.Logging;

// The same bootstrap-logger window the API covers: a failure while the container is being built should
// leave a line behind rather than a silent exit. AddServiceDefaults swaps in the configured logger.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // No Vault here, unlike the API and the portal. This server holds no secrets to fetch: it only calls
    // the three anonymous catalogue endpoints, and the API's address comes from service discovery.
    builder.AddServiceDefaults();

    // A client of the WebAPI over HTTP, exactly the way the MVC portal is - not a second host of the
    // Application layer. Hosting the handlers directly would have dragged in the write side's wiring:
    // the MassTransit consumers would compete for the API's queues and the search index workers would
    // run twice. One process owns the business logic; this one only presents it to AI clients.
    builder.Services.AddHttpClient<ICatalogueApiClient, CatalogueApiClient>(client =>
        client.BaseAddress = new Uri("https+http://webapi"));

    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithTools<CatalogueTools>();

    builder.Services.AddProblemDetails();

    var app = builder.Build();

    app.UseStadiaPassRequestLogging();
    app.UseExceptionHandler();

    app.MapDefaultEndpoints();
    app.MapMcp("/mcp");

    await app.RunAsync();
}
catch (Exception exception)
{
    Log.Fatal(exception, "StadiaPass MCP server terminated unexpectedly");

    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
