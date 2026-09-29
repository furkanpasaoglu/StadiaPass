using System.Text.Json;
using StadiaPass.AgentHost.Support;
using Xunit.Abstractions;

namespace StadiaPass.AgentHost.Evals;

public sealed record MergeCase(
    string Id,
    string Question,
    string Analyst,
    string Policy,
    IReadOnlyList<string> MustContain,
    IReadOnlyList<string> MustNotContain);

public static class MergeDataset
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyDictionary<string, MergeCase> Cases { get; } = Load();

    public static TheoryData<string> CaseIds
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var id in Cases.Keys)
            {
                data.Add(id);
            }

            return data;
        }
    }

    private static Dictionary<string, MergeCase> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "MergeDataset.json");
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<DatasetDocument>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("MergeDataset.json deserialized to null.");

        return document.Cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    private sealed record DatasetDocument(IReadOnlyList<MergeCase> Cases);
}

/// <summary>
/// Scores the merger against the real model, with both replies fixed so that only the merge is measured.
/// </summary>
/// <remarks>
/// Three things, each able to fail on its own: the merge was used rather than thrown away for losing a
/// citation; what had to stay stayed - facts from both sides and every [n]; and the noise went - the
/// sentence where one side says it knows nothing about the half the other side answered.
/// </remarks>
public sealed class MergeEvals(OllamaFixture ollama, ITestOutputHelper output) : IClassFixture<OllamaFixture>
{
    private static readonly TimeSpan CaseTimeout = TimeSpan.FromMinutes(3);

    [SkippableTheory]
    [MemberData(nameof(MergeDataset.CaseIds), MemberType = typeof(MergeDataset))]
    public async Task Merger_keeps_both_answers_and_every_citation_and_drops_the_noise(string caseId)
    {
        Skip.If(ollama.ChatClient is null, ollama.SkipReason);

        var evalCase = MergeDataset.Cases[caseId];
        var merger = new SupportMerger(ollama.ChatClient);

        using var timeout = new CancellationTokenSource(CaseTimeout);

        var merged = await merger.MergeAsync(evalCase.Question, evalCase.Analyst, evalCase.Policy, timeout.Token);

        output.WriteLine($"Q: {evalCase.Question}");
        output.WriteLine($"Merged: {merged}");

        var fallback = evalCase.Analyst + "\n\n" + evalCase.Policy;

        merged.Should().NotBe(
            fallback,
            "the merge was thrown away, which means it lost a citation or came back empty");

        foreach (var kept in evalCase.MustContain)
        {
            merged.Should().ContainEquivalentOf(kept, $"'{kept}' was in a reply and has to survive the merge");
        }

        foreach (var dropped in evalCase.MustNotContain)
        {
            merged.Should().NotContainEquivalentOf(
                dropped,
                $"'{dropped}' is one side saying it knows nothing about a part the other side answered");
        }
    }
}
