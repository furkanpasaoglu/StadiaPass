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
/// A case may also carry a <see cref="EvalCase.History"/>: the question then arrives mid-conversation,
/// with an earlier tool result in front of the model - the only way to score whether it reads a figure
/// again or serves the stale one it already has.
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
            .. Replay(evalCase.History),
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

    /// <summary>
    /// Rebuilds a recorded conversation as the messages a real turn would have left behind: an assistant
    /// message carrying the call, then the tool message carrying its result, paired by call id the way
    /// every chat API demands. Without the pairing the history is malformed and the model's refusal to
    /// reuse it would prove nothing.
    /// </summary>
    private static IEnumerable<ChatMessage> Replay(IReadOnlyList<PriorTurn>? history)
    {
        if (history is null)
        {
            yield break;
        }

        var callNumber = 0;

        foreach (var turn in history)
        {
            if (turn.User is { } user)
            {
                yield return new ChatMessage(ChatRole.User, user);
            }

            if (turn.ToolCall is { } toolCall)
            {
                var callId = $"call-{++callNumber}";
                var arguments = toolCall.Arguments?.ToDictionary(
                    argument => argument.Key,
                    argument => (object?)argument.Value);

                yield return new ChatMessage(
                    ChatRole.Assistant,
                    [new FunctionCallContent(callId, toolCall.Tool, arguments)]);

                yield return new ChatMessage(
                    ChatRole.Tool,
                    [new FunctionResultContent(callId, toolCall.Returned)]);
            }

            if (turn.Assistant is { } assistant)
            {
                yield return new ChatMessage(ChatRole.Assistant, assistant);
            }
        }
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
