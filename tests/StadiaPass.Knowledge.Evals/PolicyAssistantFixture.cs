using System.Net.Http.Json;

namespace StadiaPass.Knowledge.Evals;

/// <summary>
/// The running agent host, reached over HTTP, or a reason the evals are being skipped.
/// </summary>
/// <remarks>
/// Opt-in, like the analyst's evals: they need the whole stack up - PostgreSQL with the chunks in it,
/// the MCP server, the agent host and a local model - and a test run on a machine without them must say
/// "skipped", not "failed". The one thing done here beyond probing is a warm-up question, because the first
/// call after a quiet spell loads the model and would otherwise be the one case that times out.
/// </remarks>
public sealed class PolicyAssistantFixture : IDisposable
{
    public PolicyAssistantFixture()
    {
        if (Environment.GetEnvironmentVariable("STADIAPASS_RUN_POLICY_EVALS") is not "1")
        {
            SkipReason = "Policy evals are opt-in: set STADIAPASS_RUN_POLICY_EVALS=1 to run them.";

            return;
        }

        var endpoint = Environment.GetEnvironmentVariable("STADIAPASS_POLICY_EVAL_URL") ?? "http://localhost:5399";

        Client = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromMinutes(3) };

        try
        {
            using var health = Client.GetAsync("/health").GetAwaiter().GetResult();

            health.EnsureSuccessStatusCode();

            // Warm-up: loads the embedding and chat models so the first scored case is measured, not waited for.
            AskAsync("Koltuk kaç dakika tutulur?", CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            SkipReason = $"The agent host is not answering at {endpoint}: {exception.Message}";
            Client.Dispose();
            Client = null;
        }
    }

    public HttpClient? Client { get; private set; }

    public string? SkipReason { get; }

    public async Task<PolicyAnswer> AskAsync(string question, CancellationToken cancellationToken)
    {
        using var response = await Client!.PostAsJsonAsync("/policy/ask", new { question }, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PolicyAnswer>(cancellationToken)
            ?? throw new InvalidOperationException("The agent host answered with an empty body.");
    }

    public void Dispose() => Client?.Dispose();
}

/// <summary>Wire-shape mirror of the agent host's answer.</summary>
public sealed record PolicyAnswer(string Question, string Answer, IReadOnlyList<PolicySource> Sources);

public sealed record PolicySource(int Number, string Document, string Title, string Heading, double Score);
