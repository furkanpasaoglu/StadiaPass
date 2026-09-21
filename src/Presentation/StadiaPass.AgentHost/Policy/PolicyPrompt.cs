using System.Text;

namespace StadiaPass.AgentHost.Policy;

/// <summary>
/// Lays the retrieved passages and the question out as the one user message the model reads.
/// </summary>
/// <remarks>
/// Passages first, numbered, each under a line saying where it is from; the question last, because a
/// model answers what it read most recently. The number is what the answer cites, so it is the same
/// number the caller gets back beside the sources.
/// </remarks>
public static class PolicyPrompt
{
    public static string Build(string question, IReadOnlyList<PolicyPassage> passages)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("Passages from the policy documents, nearest to the question first:");
        prompt.AppendLine();

        for (var index = 0; index < passages.Count; index++)
        {
            var passage = passages[index];
            var label = $"{passage.Title} > {passage.Heading}";

            prompt.Append('[').Append(index + 1).Append("] ").AppendLine(label);
            prompt.AppendLine(BodyOf(passage, label));
            prompt.AppendLine();
        }

        prompt.AppendLine("Question:");
        prompt.Append(question);

        return prompt.ToString();
    }

    /// <summary>
    /// The passage without the context line it starts with. Every chunk was written as "Document >
    /// Heading" plus a blank line plus the text, for the embedding's sake; the prompt has just labelled
    /// the passage with that same line, and the model does not need to read it twice.
    /// </summary>
    private static string BodyOf(PolicyPassage passage, string label)
    {
        var text = passage.Text.TrimStart();

        return text.StartsWith(label, StringComparison.Ordinal)
            ? text[label.Length..].TrimStart()
            : text;
    }
}
