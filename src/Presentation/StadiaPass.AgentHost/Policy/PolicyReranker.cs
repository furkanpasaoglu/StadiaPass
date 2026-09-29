using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace StadiaPass.AgentHost.Policy;

/// <summary>
/// Reads the retrieved passages next to the question and says which of them actually help, best first.
/// </summary>
/// <remarks>
/// <para>
/// Retrieval compares two summaries - the question's vector and each passage's vector - made separately. It
/// is fast and it is blunt. Reranking is the second, slower look: the question and every candidate read side
/// by side, and the ones that answer it moved to the front. Retrieval is widened to ten candidates so that a
/// section ranked fourth or seventh by the vectors can still make it into the three the model reads.
/// </para>
/// <para>
/// The usual reranker is a small cross-encoder model. Ollama has no endpoint for one yet (0.30 answers 404 on
/// /api/rerank), so the chat model does the job instead: one call, the passages numbered, the numbers of the
/// useful ones back. It is the listwise way of reranking with a language model.
/// </para>
/// <para>
/// Whatever the model replies, the result is never worse-shaped than plain retrieval: the passages it names go
/// first, the rest are filled in retrieval order up to the same count as before, and a reply that names
/// nothing usable leaves retrieval's order untouched.
/// </para>
/// </remarks>
internal sealed partial class PolicyReranker(IChatClient chatClient)
{
    public const string Instructions =
        "You help choose which passages from the ticketing rule documents to show to an assistant that "
        + "will answer a staff question. You are given the question and numbered passages. Reply with the "
        + "numbers of the passages that help answer the question - that state the rule asked about, or its "
        + "exception - most useful first, separated by commas, and nothing else. Leave out passages about a "
        + "nearby topic that do not help. If none of them helps, reply 0.";

    public async Task<IReadOnlyList<PolicyPassage>> RerankAsync(
        string question,
        IReadOnlyList<PolicyPassage> candidates,
        int take,
        CancellationToken cancellationToken)
    {
        if (candidates.Count <= take)
        {
            return candidates.ToList();
        }

        var options = new ChatOptions
        {
            Instructions = Instructions,
            // The same question has to choose the same passages every time, or the evals measure luck.
            Temperature = 0f
        };

        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, BuildPrompt(question, candidates)) };

        var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);

        return Choose(response.Text, candidates, take);
    }

    /// <summary>
    /// The passages the reply names, in its order, then the rest in retrieval order, up to <paramref name="take"/>.
    /// Numbers that are repeated, out of range or zero are ignored.
    /// </summary>
    private static List<PolicyPassage> Choose(string? reply, IReadOnlyList<PolicyPassage> candidates, int take)
    {
        var chosen = new List<PolicyPassage>();

        foreach (Match match in NumberPattern().Matches(reply ?? string.Empty))
        {
            var number = int.Parse(match.Value, CultureInfo.InvariantCulture);

            if (number < 1 || number > candidates.Count)
            {
                continue;
            }

            var passage = candidates[number - 1];

            if (chosen.Contains(passage))
            {
                continue;
            }

            chosen.Add(passage);

            if (chosen.Count == take)
            {
                return chosen;
            }
        }

        foreach (var passage in candidates)
        {
            if (chosen.Count == take)
            {
                break;
            }

            if (!chosen.Contains(passage))
            {
                chosen.Add(passage);
            }
        }

        return chosen;
    }

    private static string BuildPrompt(string question, IReadOnlyList<PolicyPassage> candidates)
    {
        var prompt = new StringBuilder()
            .AppendLine("Question:")
            .AppendLine(question)
            .AppendLine()
            .AppendLine("Passages:");

        for (var index = 0; index < candidates.Count; index++)
        {
            prompt.AppendLine()
                .Append('[').Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append("] ")
                .AppendLine(candidates[index].Text);
        }

        return prompt.ToString();
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberPattern();
}
