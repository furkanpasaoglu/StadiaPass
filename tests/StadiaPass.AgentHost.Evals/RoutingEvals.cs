using StadiaPass.AgentHost.Support;
using Xunit.Abstractions;

namespace StadiaPass.AgentHost.Evals;

/// <summary>
/// Scores the support router against the real model, through the same guardrail production runs.
/// </summary>
/// <remarks>
/// Only the choice is scored, never an answer: whether the question would reach the analyst, the policy
/// assistant or neither. The cases that matter are the pairs in the dataset, where the same word - cancel,
/// refund, price - belongs to a different topic depending on what is actually being asked.
/// </remarks>
public sealed class RoutingEvals(OllamaFixture ollama, ITestOutputHelper output) : IClassFixture<OllamaFixture>
{
    private static readonly TimeSpan CaseTimeout = TimeSpan.FromMinutes(3);

    [SkippableTheory]
    [MemberData(nameof(RoutingDataset.CaseIds), MemberType = typeof(RoutingDataset))]
    public async Task Router_sends_the_question_to_the_right_assistant(string caseId)
    {
        Skip.If(ollama.ChatClient is null, ollama.SkipReason);

        var evalCase = RoutingDataset.Cases[caseId];
        var router = new SupportRouter(ollama.ChatClient);

        using var timeout = new CancellationTokenSource(CaseTimeout);

        var topic = await router.RouteAsync(evalCase.Question, timeout.Token);

        output.WriteLine($"Q: {evalCase.Question}");
        output.WriteLine($"Routed to: {topic} (expected {evalCase.ExpectedTopic})");

        topic.Should().Be(evalCase.ExpectedTopic, $"'{evalCase.Question}' belongs to {evalCase.ExpectedTopic}");
    }
}
