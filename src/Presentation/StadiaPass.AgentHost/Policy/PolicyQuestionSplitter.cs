using Microsoft.Extensions.AI;

namespace StadiaPass.AgentHost.Policy;

/// <summary>
/// Finds the separate rule questions inside one message, so each can be retrieved for on its own.
/// </summary>
/// <remarks>
/// <para>
/// A message that asks several things turns into one blurred vector. Measured on a four-question paragraph:
/// the three sections it needed came back 3rd, 10th and 12th of 26, with every score squeezed between 0.55
/// and 0.69, so only one of them reached the model. Asked one at a time, each of the three questions found
/// its section first, with a clear margin. This class does the splitting; the assistant does the rest.
/// </para>
/// <para>
/// It lists only questions about the rules. What a message asks about specific matches belongs to the
/// analyst, and a greeting belongs to nobody; both are left out, because a retrieval for them can only bring
/// back a section that looks relevant and is not.
/// </para>
/// </remarks>
internal sealed class PolicyQuestionSplitter(IChatClient chatClient)
{
    /// <summary>
    /// Four is more than staff ask in one breath, and it keeps a pasted wall of text from turning into a dozen
    /// retrievals and a prompt nobody can read.
    /// </summary>
    public const int MaxQuestions = 4;

    public const string Instructions =
        "You read a message from ticketing staff and list the separate questions it asks about the "
        + "ticketing RULES: refunds, what happens when a match is cancelled, seat holds, payment, how a "
        + "ticket price is set, who may buy a ticket for whom, what staff may do, stadium entry and "
        + "forbidden items. Write each one as a short question that stands on its own, one per line, in "
        + "the language of the message, and nothing else - no numbering, no explanation. Leave out "
        + "anything about specific matches (which matches are on, seats left, prices of a given match, "
        + "sales figures) and greetings or thanks. If the message asks only one rule question, write that "
        + "one question. If it asks none, write the message unchanged.";

    public async Task<IReadOnlyList<string>> SplitAsync(string question, CancellationToken cancellationToken)
    {
        var options = new ChatOptions
        {
            Instructions = Instructions,
            // The same message has to split the same way every time, or retrieval does not repeat.
            Temperature = 0f
        };

        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, question) };

        var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);

        return ReadQuestions(response.Text, question);
    }

    /// <summary>
    /// Reads one question per line, forgiving the numbering and bullets a model adds anyway. A reply with no
    /// question in it falls back to the message itself, which is exactly what happened before splitting.
    /// </summary>
    public static IReadOnlyList<string> ReadQuestions(string? reply, string original)
    {
        var questions = new List<string>();

        foreach (var rawLine in (reply ?? string.Empty).Split('\n'))
        {
            var line = StripListMarker(rawLine.Trim());

            if (line.Length == 0)
            {
                continue;
            }

            var alreadyListed = questions.Any(question => string.Equals(question, line, StringComparison.OrdinalIgnoreCase));

            if (alreadyListed)
            {
                continue;
            }

            questions.Add(line);

            if (questions.Count == MaxQuestions)
            {
                break;
            }
        }

        if (questions.Count == 0)
        {
            questions.Add(original);
        }

        return questions;
    }

    /// <summary>Takes a leading "-", "*", "•", "1." or "1)" off a line.</summary>
    private static string StripListMarker(string line)
    {
        if (line.StartsWith('-') || line.StartsWith('*') || line.StartsWith('•'))
        {
            return line.Substring(1).Trim();
        }

        var digits = 0;

        while (digits < line.Length && char.IsDigit(line[digits]))
        {
            digits++;
        }

        var hasNumberMarker = digits > 0
            && digits < line.Length
            && (line[digits] == '.' || line[digits] == ')');

        if (hasNumberMarker)
        {
            return line.Substring(digits + 1).Trim();
        }

        return line;
    }
}
