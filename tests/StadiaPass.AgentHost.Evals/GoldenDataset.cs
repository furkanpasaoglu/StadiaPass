using System.Text.Json;

namespace StadiaPass.AgentHost.Evals;

/// <summary>One question and every (tool, arguments) outcome that counts as the model choosing well.</summary>
public sealed record EvalCase(string Id, string Question, IReadOnlyList<AcceptableCall> Acceptable);

/// <summary>
/// A predicate over one tool call: the tool by exact name, each listed argument by case-insensitive
/// substring. Substrings, deliberately - the model phrasing "Fenerbahce" where the admin typed
/// "Fenerbahçe" is a pass, because the analyzer behind the real tool folds the difference away anyway.
/// </summary>
public sealed record AcceptableCall(string Tool, IReadOnlyDictionary<string, string>? Arguments = null);

public static class GoldenDataset
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyDictionary<string, EvalCase> Cases { get; } = Load();

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

    private static Dictionary<string, EvalCase> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GoldenDataset.json");
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<DatasetDocument>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("GoldenDataset.json deserialized to null.");

        return document.Cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    private sealed record DatasetDocument(IReadOnlyList<EvalCase> Cases);
}
