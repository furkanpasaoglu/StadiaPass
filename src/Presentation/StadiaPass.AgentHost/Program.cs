using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using OllamaSharp;
using Serilog;
using StadiaPass.AgentHost;
using StadiaPass.ServiceDefaults.Logging;

// Same bootstrap-logger window as every other host in the solution.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // No Vault: like the MCP server, this host holds no secrets. The model runs on the developer's own
    // machine and the only thing it talks to is the MCP server, whose address the AppHost hands over.
    builder.AddServiceDefaults();

    builder.Services
        .AddOptions<AgentOptions>()
        .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // The agent never sees "Ollama" - it sees IChatClient. That seam is the whole point: the day this
    // moves to a cloud model, the registration below is the only line that changes.
    builder.Services.AddSingleton<IChatClient>(provider =>
    {
        var options = provider.GetRequiredService<IOptions<AgentOptions>>().Value;

        return new OllamaApiClient(new Uri(options.OllamaEndpoint), options.Model);
    });

    // The tools come from our own MCP server - the same three catalogue tools Claude used, consumed by a
    // second client. One tool layer, many consumers: nothing below this host had to change to make an
    // internal agent exist, and nothing here duplicates a business rule.
    builder.Services.AddSingleton(provider =>
    {
        var options = provider.GetRequiredService<IOptions<AgentOptions>>().Value;

        return ConnectToMcpAsync(options.McpEndpoint).GetAwaiter().GetResult();
    });

    builder.AddAIAgent(AnalystAgent.Name, (provider, _) =>
    {
        var tools = provider.GetRequiredService<IList<McpClientTool>>();

        return new ChatClientAgent(
            provider.GetRequiredService<IChatClient>(),
            new ChatClientAgentOptions
            {
                Name = AnalystAgent.Name,
                ChatOptions = new ChatOptions
                {
                    Instructions = AnalystAgent.Instructions,
                    // An analyst reports; it does not improvise. Determinism first, personality never.
                    Temperature = 0f,
                    Tools = [.. tools.Cast<AITool>()]
                }
            });
    });

    // DevUI and the OpenAI-compatible endpoints it drives. Development-only by design - this is the
    // playground in front of the agent, not the product; the admin panel comes later and comes separately.
    builder.AddOpenAIResponses();
    builder.AddOpenAIConversations();

    if (builder.Environment.IsDevelopment())
    {
        builder.AddDevUI();
    }

    var app = builder.Build();

    app.UseStadiaPassRequestLogging();

    app.MapDefaultEndpoints();
    app.MapOpenAIResponses();
    app.MapOpenAIConversations();

    if (app.Environment.IsDevelopment())
    {
        app.MapDevUI();
    }

    await app.RunAsync();
}
catch (Exception exception)
{
    Log.Fatal(exception, "StadiaPass agent host terminated unexpectedly");

    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// The MCP server is health-checked before this host starts, but "healthy" and "finished binding" are not
// the same instant; a few patient retries cover the gap without hiding a server that is genuinely gone.
static async Task<IList<McpClientTool>> ConnectToMcpAsync(string endpoint)
{
    const int attempts = 5;

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint),
                Name = "StadiaPass MCP"
            }));

            return await client.ListToolsAsync();
        }
        catch (Exception exception) when (attempt < attempts && exception is HttpRequestException or IOException)
        {
            Log.Warning(
                "MCP server at {Endpoint} not answering yet (attempt {Attempt}/{Attempts}); retrying",
                endpoint, attempt, attempts);

            await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
        }
    }
}
