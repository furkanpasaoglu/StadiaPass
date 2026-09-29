using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace StadiaPass.AgentHost.Support;

/// <summary>
/// Turns the analyst's reply and the policy assistant's reply to a mixed question into one answer.
/// </summary>
/// <remarks>
/// <para>
/// Both assistants read the whole question, so each one also notices the half that is not its business and
/// may say it cannot answer it - "the documents say nothing about upcoming matches" right under the list of
/// upcoming matches. Put side by side, the two replies read like a contradiction. One more model call reads
/// both, keeps what each got right, drops what each could not answer, and says plainly if some part of the
/// question was answered by neither.
/// </para>
/// <para>
/// Rules are taken from the policy reply alone. The analyst never sees the rule documents, and in a live run
/// with a four-question paragraph it answered a rule question anyway, from nowhere - a merge that kept it
/// would have told staff an uncited rule. The instructions say so, and an eval case holds them to it.
/// </para>
/// <para>
/// That call is the one place a policy citation could go missing, and a sentence that lost its [n] is a
/// sentence nobody can check. So the merge is checked before it is used: every citation in the policy
/// reply has to still be there. If one is not, the merge is thrown away and the two replies are shown as
/// they were - less tidy, but every sentence still points at its source.
/// </para>
/// </remarks>
internal sealed partial class SupportMerger(IChatClient chatClient)
{
    public const string Instructions =
        "You combine two replies written for the same staff question into one answer. The first reply "
        + "comes from an analyst who knows the matches: what is on, seats, prices, sales. The second comes "
        + "from a policy assistant who knows the rules, and cites the rule documents with numbers in square "
        + "brackets such as [1]. Use ONLY what the two replies say; add no fact of your own. "
        + "Take the facts about matches from the analyst reply, and take the RULES ONLY from the policy "
        + "reply. The analyst never sees the rule documents, so anything the analyst reply says about a "
        + "rule or a procedure - refunds, cancellations, who may buy a ticket for whom, seat holds, payment, "
        + "what may be brought into the stadium - is a guess: leave it out entirely, even when the policy "
        + "reply says nothing about that rule. "
        + "Each reply may say it has no information about the part of the question the OTHER reply answers; "
        + "leave such sentences out, because the other reply has answered that part. "
        + "Every sentence you take from the policy reply keeps its citation exactly as written - [1] stays "
        + "[1] - and you never add, remove or renumber a citation. "
        + "If some part of the question is answered by neither reply, say which part, by name, in one short "
        + "sentence - for example 'Şemsiye konusunda belgelerde bilgi yok.' - never a bare 'no information on "
        + "this' that leaves the reader guessing which part is meant. "
        + "Answer in the language of the question, briefly, as one answer: the matches first, then the "
        + "rules. Do not mention that there were two replies or two assistants.";

    public async Task<string> MergeAsync(
        string question,
        string analystReply,
        string policyReply,
        CancellationToken cancellationToken)
    {
        var prompt = new StringBuilder()
            .AppendLine("Question:")
            .AppendLine(question)
            .AppendLine()
            .AppendLine("Analyst reply:")
            .AppendLine(analystReply)
            .AppendLine()
            .AppendLine("Policy reply:")
            .AppendLine(policyReply)
            .ToString();

        var options = new ChatOptions
        {
            Instructions = Instructions,
            // A merge is an edit, not a composition. Determinism first.
            Temperature = 0f
        };

        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, prompt) };

        var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
        var merged = response.Text.Trim();

        if (merged.Length == 0 || !KeepsEveryCitation(policyReply, merged))
        {
            return analystReply + "\n\n" + policyReply;
        }

        return merged;
    }

    /// <summary>Every [n] in the text, as numbers.</summary>
    public static HashSet<int> CitationsOf(string text)
    {
        var numbers = new HashSet<int>();

        foreach (Match match in CitationPattern().Matches(text))
        {
            numbers.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        return numbers;
    }

    private static bool KeepsEveryCitation(string policyReply, string merged) =>
        CitationsOf(policyReply).IsSubsetOf(CitationsOf(merged));

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex CitationPattern();
}
