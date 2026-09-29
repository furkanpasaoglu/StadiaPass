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
using StadiaPass.AgentHost.Guardrails;
using StadiaPass.AgentHost.Policy;
using StadiaPass.AgentHost.Support;
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

    builder.Services.AddSingleton<GuardrailMetrics>();

    builder.Services.AddSingleton<IChatClient>(provider =>
    {
        var options = provider.GetRequiredService<IOptions<AgentOptions>>().Value;

        // The cast settles an overload ambiguity: OllamaApiClient is also an IEmbeddingGenerator.
        return ((IChatClient)new OllamaApiClient(new Uri(options.OllamaEndpoint), options.Model))
            .AsBuilder()
            .Use(inner => new PersonalDataGuardrail(
                inner,
                provider.GetRequiredService<GuardrailMetrics>(),
                provider.GetRequiredService<ILogger<PersonalDataGuardrail>>()))
            .UseOpenTelemetry(
                provider.GetRequiredService<ILoggerFactory>(),
                sourceName: "StadiaPass.Agent")
            .Build(provider);
    });

    // The tools come from our own MCP server - the same tools any other MCP client is offered, consumed by a
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
                    Tools = [.. AnalystAgent.ToolsFrom(tools)]
                }
            });
    });

    // The policy assistant: retrieval in code through the MCP server's search tool, then the same
    // guarded, metered chat client the analyst runs on. Not registered as an agent - it has no tools to
    // choose between, and DevUI would offer it a chat box that skipped the retrieval it exists for.
    builder.Services.AddSingleton<IPolicyRetriever, McpPolicyRetriever>();
    builder.Services.AddSingleton<PolicyAssistant>();

    // The support desk: one door in front of both. A router picks the analyst, the policy assistant or -
    // when the question asks both things - both at once, as the branches of a workflow, and a merger turns
    // those two replies into one answer.
    builder.Services.AddSingleton<SupportRouter>();
    builder.Services.AddSingleton<SupportMerger>();
    builder.Services.AddSingleton<IAnalyst>(provider =>
        new AgentAnalyst(provider.GetRequiredKeyedService<AIAgent>(AnalystAgent.Name)));
    builder.Services.AddSingleton<SupportDesk>();

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

    // Staff-facing, like everything on this host, and unauthenticated for the same reason the DevUI is:
    // the host is not exposed beyond the developer's machine. The day it is, the token the portal already
    // carries is what this endpoint checks.
    app.MapPost("/policy/ask", async (PolicyQuestion request, PolicyAssistant assistant, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(request.Question)
            ? Results.ValidationProblem(new Dictionary<string, string[]> { ["question"] = ["A question is required."] })
            : Results.Ok(await assistant.AskAsync(request.Question.Trim(), cancellationToken)))
        .WithName("AskPolicy")
        .WithSummary("Answers a staff question from the policy documents, citing the passages it read.");

    app.MapPost("/support/ask", async (PolicyQuestion request, SupportDesk desk, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(request.Question)
            ? Results.ValidationProblem(new Dictionary<string, string[]> { ["question"] = ["A question is required."] })
            : Results.Ok(await desk.AskAsync(request.Question.Trim(), cancellationToken)))
        .WithName("AskSupport")
        .WithSummary("Routes a staff question to the analyst, the policy assistant or both, and returns every reply.");

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

/// <summary>The body of a staff question, for the policy assistant and for the support desk alike.</summary>
internal sealed record PolicyQuestion(string Question);
