using System.Diagnostics.Metrics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OllamaSharp;
using StadiaPass.AgentHost.Guardrails;

namespace StadiaPass.AgentHost.Evals;

/// <summary>
/// The local model behind the evals, and the two reasons a run may be skipped instead of failed:
/// evals are opt-in (set <c>STADIAPASS_RUN_EVALS=1</c>) so twenty model calls never tax the ordinary
/// <c>dotnet test</c> loop, and an unreachable Ollama is a missing prerequisite, not a red build.
/// A skipped eval reports as skipped - it never silently passes.
/// </summary>
/// <remarks>
/// The guardrail stands in front of the model here exactly as it does in production, for the same reason
/// the instructions are single-sourced: a question with an address still in it is not something the model
/// ever sees, so an eval that showed it one would be scoring a pipeline nobody runs. It also turns the
/// existing cases into an answer to a question worth asking - whether putting a redactor in the path
/// changes any answer that had nothing to redact.
/// </remarks>
public sealed class OllamaFixture : IDisposable
{
    private readonly ServiceProvider? _meters;

    public OllamaFixture()
    {
        if (Environment.GetEnvironmentVariable("STADIAPASS_RUN_EVALS") is not "1")
        {
            SkipReason = "Evals are opt-in: set STADIAPASS_RUN_EVALS=1 to run them.";

            return;
        }

        var endpoint = Environment.GetEnvironmentVariable("STADIAPASS_EVAL_OLLAMA") ?? "http://localhost:11434";
        Model = Environment.GetEnvironmentVariable("STADIAPASS_EVAL_MODEL") ?? "qwen3:30b-a3b";

        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        try
        {
            using var response = probe.GetAsync(new Uri(new Uri(endpoint), "/api/version")).GetAwaiter().GetResult();

            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            SkipReason = $"Ollama is not answering at {endpoint}: {exception.Message}";

            return;
        }

        // A real meter factory rather than a stand-in: the counter the guardrail writes to is part of what
        // is being exercised, and a fake here would be a second thing that could be wrong.
        _meters = new ServiceCollection().AddMetrics().BuildServiceProvider();

        ChatClient = ((IChatClient)new OllamaApiClient(new Uri(endpoint), Model))
            .AsBuilder()
            .Use(inner => new PersonalDataGuardrail(
                inner,
                new GuardrailMetrics(_meters.GetRequiredService<IMeterFactory>()),
                NullLogger<PersonalDataGuardrail>.Instance))
            .Build();
    }

    public IChatClient? ChatClient { get; }

    public string Model { get; } = "";

    public string? SkipReason { get; }

    public void Dispose()
    {
        (ChatClient as IDisposable)?.Dispose();
        _meters?.Dispose();
    }
}
