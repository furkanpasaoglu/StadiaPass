using Microsoft.Extensions.AI;
using OllamaSharp;

namespace StadiaPass.AgentHost.Evals;

/// <summary>
/// The local model behind the evals, and the two reasons a run may be skipped instead of failed:
/// evals are opt-in (set <c>STADIAPASS_RUN_EVALS=1</c>) so twenty model calls never tax the ordinary
/// <c>dotnet test</c> loop, and an unreachable Ollama is a missing prerequisite, not a red build.
/// A skipped eval reports as skipped - it never silently passes.
/// </summary>
public sealed class OllamaFixture : IDisposable
{
    public OllamaFixture()
    {
        if (Environment.GetEnvironmentVariable("STADIAPASS_RUN_EVALS") is not "1")
        {
            SkipReason = "Evals are opt-in: set STADIAPASS_RUN_EVALS=1 to run them.";

            return;
        }

        var endpoint = Environment.GetEnvironmentVariable("STADIAPASS_EVAL_OLLAMA") ?? "http://localhost:11434";
        Model = Environment.GetEnvironmentVariable("STADIAPASS_EVAL_MODEL") ?? "qwen2.5:14b";

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

        ChatClient = new OllamaApiClient(new Uri(endpoint), Model);
    }

    public IChatClient? ChatClient { get; }

    public string Model { get; } = "";

    public string? SkipReason { get; }

    public void Dispose() => (ChatClient as IDisposable)?.Dispose();
}
