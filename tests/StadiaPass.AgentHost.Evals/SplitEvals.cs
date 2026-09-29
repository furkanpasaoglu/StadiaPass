using System.Text.Json;
using StadiaPass.AgentHost.Policy;
using Xunit.Abstractions;

namespace StadiaPass.AgentHost.Evals;

public sealed record SplitCase(string Id, string Question, int ExpectedCount, IReadOnlyList<string> MustCover);

public static class SplitDataset
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyDictionary<string, SplitCase> Cases { get; } = Load();

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

    private static Dictionary<string, SplitCase> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SplitDataset.json");
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<DatasetDocument>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("SplitDataset.json deserialized to null.");

        return document.Cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    private sealed record DatasetDocument(IReadOnlyList<SplitCase> Cases);
}

/// <summary>
/// Scores the policy question splitter against the real model: the right number of rule questions, and
/// every rule the message asked about present in one of them.
/// </summary>
/// <remarks>
/// A split that is one short leaves a rule unretrieved - the problem this exists to fix. A split that is one
/// long, for a message with one rule question, changes retrieval for the cases every other eval measures.
/// Both are failures, which is why the count is held exactly.
/// </remarks>
public sealed class SplitEvals(OllamaFixture ollama, ITestOutputHelper output) : IClassFixture<OllamaFixture>
{
    private static readonly TimeSpan CaseTimeout = TimeSpan.FromMinutes(3);

    [SkippableTheory]
    [MemberData(nameof(SplitDataset.CaseIds), MemberType = typeof(SplitDataset))]
    public async Task Splitter_lists_each_rule_question_and_nothing_else(string caseId)
    {
        Skip.If(ollama.ChatClient is null, ollama.SkipReason);

        var evalCase = SplitDataset.Cases[caseId];
        var splitter = new PolicyQuestionSplitter(ollama.ChatClient);

        using var timeout = new CancellationTokenSource(CaseTimeout);

        var questions = await splitter.SplitAsync(evalCase.Question, timeout.Token);

        output.WriteLine($"Q: {evalCase.Question}");
        output.WriteLine("Split into:");
        output.WriteLine(string.Join(Environment.NewLine, questions.Select(question => "  - " + question)));

        questions.Should().HaveCount(
            evalCase.ExpectedCount,
            "the message asks exactly this many questions about the rules");

        foreach (var word in evalCase.MustCover)
        {
            questions.Should().Contain(
                question => question.Contains(word, StringComparison.OrdinalIgnoreCase),
                $"the message asks about '{word}', so one of the split questions has to");
        }
    }
}
