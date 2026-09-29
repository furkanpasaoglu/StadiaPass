using StadiaPass.AgentHost.Policy;

namespace StadiaPass.AgentHost.Support;

/// <summary>What one assistant said, and - for the policy assistant - the passages it read.</summary>
public sealed record SupportReply(string Assistant, string Answer, IReadOnlyList<PolicySource> Sources);

/// <summary>
/// The desk's answer: the question as it was read (personal data masked), where it was routed, the one
/// answer to show, the policy passages its citations point at, and - for whoever is debugging - the reply
/// of every assistant that was asked, as it came back.
/// </summary>
/// <param name="Answer">
/// The reply itself when one assistant answered; the merge of both when the question asked both things.
/// </param>
public sealed record SupportAnswer(
    string Question,
    string Topic,
    string Answer,
    IReadOnlyList<PolicySource> Sources,
    IReadOnlyList<SupportReply> Replies);

/// <summary>A question after routing: what travels along the edges of the workflow.</summary>
internal sealed record RoutedQuestion(string Question, SupportTopic Topic);
