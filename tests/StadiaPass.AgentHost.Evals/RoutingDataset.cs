using System.Text.Json;
using StadiaPass.AgentHost.Support;

namespace StadiaPass.AgentHost.Evals;

public sealed record RoutingCase(string Id, string Question, string Expected)
{
    public SupportTopic ExpectedTopic => Enum.Parse<SupportTopic>(Expected, ignoreCase: true);
}

public static class RoutingDataset
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyDictionary<string, RoutingCase> Cases { get; } = Load();

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

    private static Dictionary<string, RoutingCase> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RoutingDataset.json");
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<DatasetDocument>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("RoutingDataset.json deserialized to null.");

        return document.Cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    private sealed record DatasetDocument(IReadOnlyList<RoutingCase> Cases);
}
