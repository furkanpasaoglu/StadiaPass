using System.Text.Json;

namespace StadiaPass.Knowledge.Evals;

/// <summary>
/// A question, and the sections any of which it may be answered from - or none, when the documents do not
/// cover it. More than one when a rule and its exception each state the answer.
/// </summary>
public sealed record PolicyEvalCase(string Id, string Question, IReadOnlyList<ExpectedSection>? Expected)
{
    public bool DocumentsCoverIt => Expected is { Count: > 0 };
}

public sealed record ExpectedSection(string Document, string Heading);

public static class GoldenPolicyQuestions
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyDictionary<string, PolicyEvalCase> Cases { get; } = Load();

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

    private static Dictionary<string, PolicyEvalCase> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GoldenPolicyQuestions.json");
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<DatasetDocument>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("GoldenPolicyQuestions.json deserialized to null.");

        return document.Cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    private sealed record DatasetDocument(IReadOnlyList<PolicyEvalCase> Cases);
}
