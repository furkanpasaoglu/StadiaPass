using System.Text.Json;

namespace StadiaPass.AgentHost.Evals;

/// <summary>One question and every (tool, arguments) outcome that counts as the model choosing well.</summary>
/// <param name="History">
/// The conversation the question arrives into; absent for the single-turn cases. A tool result already
/// sitting in the history is a figure the model can reuse instead of reading again, and that temptation
/// cannot be measured in one turn - which is why a stale answer survived twenty-eight of them.
/// </param>
public sealed record EvalCase(
    string Id,
    string Question,
    IReadOnlyList<AcceptableCall> Acceptable,
    IReadOnlyList<PriorTurn>? History = null);

/// <summary>
/// One turn before the question under test: something the user said, something the analyst answered, or a
/// tool the analyst called and what came back. Exactly one of the three is set.
/// </summary>
public sealed record PriorTurn(string? User = null, string? Assistant = null, RecordedCall? ToolCall = null);

/// <summary>
/// A tool call that already happened, with the result the server gave for it. The result is written as
/// real JSON in the dataset rather than an escaped string, so it stays readable and stays honest - it is
/// the shape production returns, timestamp and all.
/// </summary>
public sealed record RecordedCall(
    string Tool,
    IReadOnlyDictionary<string, string>? Arguments,
    JsonElement Returned);

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
