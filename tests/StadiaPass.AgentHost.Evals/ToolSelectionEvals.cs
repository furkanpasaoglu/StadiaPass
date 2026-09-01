using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit.Abstractions;

namespace StadiaPass.AgentHost.Evals;

/// <summary>
/// Scores the analyst's tool selection against the golden dataset: for each question, did the model pick
/// an acceptable tool with acceptable arguments - or, for small talk, keep its hands off the tools
/// entirely. The answer's prose is deliberately not judged; prose is taste, selection is correctness,
/// and a wrong selection is wrong no matter how well it reads.
/// </summary>
/// <remarks>
/// The model sees exactly what production shows it: the same instructions (<see cref="AnalystAgent"/>),
/// the same tool schemas (<see cref="ToolSurface"/>), temperature 0. One case is one test, so a prompt
/// edit that breaks Turkish search shows up as `search-tr-*` failures, not as a vague lower score.
/// </remarks>
public sealed class ToolSelectionEvals(OllamaFixture ollama, ITestOutputHelper output)
    : IClassFixture<OllamaFixture>
{
    private static readonly TimeSpan CaseTimeout = TimeSpan.FromMinutes(3);

    [SkippableTheory]
    [MemberData(nameof(GoldenDataset.CaseIds), MemberType = typeof(GoldenDataset))]
    public async Task Model_chooses_an_acceptable_tool(string caseId)
    {
        Skip.If(ollama.ChatClient is null, ollama.SkipReason);

        var evalCase = GoldenDataset.Cases[caseId];

        using var timeout = new CancellationTokenSource(CaseTimeout);

        List<ChatMessage> messages =
        [
            new(ChatRole.System, AnalystAgent.Instructions),
            new(ChatRole.User, evalCase.Question)
        ];

        var response = await ollama.ChatClient.GetResponseAsync(
            messages,
            new ChatOptions { Temperature = 0f, Tools = [.. ToolSurface.Tools] },
            timeout.Token);

        var calls = response.Messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .ToList();

        output.WriteLine($"Q: {evalCase.Question}");
        output.WriteLine(calls.Count is 0
            ? "Model called no tool."
            : string.Join(Environment.NewLine, calls.Select(Describe)));

        if (evalCase.Acceptable.Count is 0)
        {
            calls.Should().BeEmpty("this question needs no tool, and an uncalled-for tool call is noise");

            return;
        }

        calls.Should().NotBeEmpty("this question cannot be answered without a tool");
        calls.Should().Contain(
            call => evalCase.Acceptable.Any(acceptable => Matches(call, acceptable)),
            $"expected one of: {string.Join(" | ", evalCase.Acceptable.Select(Describe))}");
    }

    private static bool Matches(FunctionCallContent call, AcceptableCall acceptable)
    {
        if (!string.Equals(call.Name, acceptable.Tool, StringComparison.Ordinal))
        {
            return false;
        }

        if (acceptable.Arguments is null)
        {
            return true;
        }

        return acceptable.Arguments.All(expected =>
            call.Arguments is not null
            && call.Arguments.FirstOrDefault(argument =>
                string.Equals(argument.Key, expected.Key, StringComparison.OrdinalIgnoreCase)) is
                { Value: { } value }
            && value.ToString()!.Contains(expected.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static string Describe(FunctionCallContent call) =>
        $"{call.Name}({JsonSerializer.Serialize(call.Arguments)})";

    private static string Describe(AcceptableCall acceptable) =>
        acceptable.Arguments is null
            ? acceptable.Tool
            : $"{acceptable.Tool}({string.Join(", ", acceptable.Arguments.Select(a => $"{a.Key}~'{a.Value}'"))})";
}
