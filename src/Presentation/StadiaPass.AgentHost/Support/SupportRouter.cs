using Microsoft.Extensions.AI;

namespace StadiaPass.AgentHost.Support;

/// <summary>
/// Reads a staff question and says which of the two assistants should answer it: the analyst, for facts
/// about fixtures, or the policy assistant, for the rules.
/// </summary>
/// <remarks>
/// <para>
/// One model call that returns one word, and nothing else. The router does not answer, retrieve or call a
/// tool; it only chooses. Keeping it that small is what makes the choice measurable on its own - a wrong
/// answer from the system can then be traced to the routing or to the assistant behind it, not both.
/// </para>
/// <para>
/// It runs on the same guarded chat client as everything else, so personal data in the question is masked
/// before the model reads it. A masked question still routes correctly: the rule being asked about does not
/// depend on whose e-mail address was in the sentence.
/// </para>
/// </remarks>
internal sealed class SupportRouter(IChatClient chatClient)
{
    public const string Instructions =
        "You route questions from StadiaPass staff to the right assistant. Reply with exactly one "
        + "lowercase word and nothing else: catalogue, policy, mixed or other. "
        + "catalogue: facts about specific matches in the system - which matches are on, coming up or "
        + "were cancelled, teams, venues, dates, seats left, what a seat in a given match costs, tickets "
        + "sold or refunded for a match, revenue and occupancy. "
        + "policy: the rules and procedures that apply to every match - what happens to the money when a "
        + "match is cancelled, whether a customer can get a refund, how long a seat is held, how payment "
        + "works, how a ticket price is set, who is allowed to do what, what box office staff may do, "
        + "stadium entry and forbidden items. "
        + "mixed: one message that asks BOTH things - a fact about a specific match AND a rule, for "
        + "example 'how many seats are left for the derby, and what happens to my money if it is "
        + "cancelled?'. Use mixed only when both parts are really asked; a question about one thing that "
        + "merely mentions a word from the other topic is not mixed. "
        + "other: greetings, thanks, small talk, and anything that is none of the above. "
        + "The same words can appear in both: 'which matches were cancelled' asks about matches and is "
        + "catalogue; 'what happens if a match is cancelled' asks about a rule and is policy. "
        + "'How much is the cheapest seat for the derby' is catalogue; 'how is a ticket price set' is "
        + "policy. The question may be in Turkish or English. A placeholder such as "
        + "[redacted email address] is deliberate; route by the rest of the question.";

    public async Task<SupportTopic> RouteAsync(string question, CancellationToken cancellationToken)
    {
        var options = new ChatOptions
        {
            Instructions = Instructions,
            // A router that answers the same question two ways is two routers. Determinism first.
            Temperature = 0f
        };

        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, question) };

        var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);

        return ReadTopic(response.Text);
    }

    /// <summary>
    /// Turns the model's reply into a topic. The reply should be one word, but a model sometimes adds a
    /// full stop, capitals or a word in front; those are forgiven. A reply that names no topic, or names
    /// more than one, is read as <see cref="SupportTopic.Other"/> rather than guessed at.
    /// </summary>
    public static SupportTopic ReadTopic(string? reply)
    {
        var text = (reply ?? string.Empty).ToLowerInvariant();

        var saysCatalogue = text.Contains("catalogue");
        var saysPolicy = text.Contains("policy");
        var saysMixed = text.Contains("mixed");
        var saysOther = text.Contains("other");

        var namedCount = 0;
        if (saysCatalogue) namedCount++;
        if (saysPolicy) namedCount++;
        if (saysMixed) namedCount++;
        if (saysOther) namedCount++;

        if (namedCount != 1)
        {
            return SupportTopic.Other;
        }

        if (saysCatalogue)
        {
            return SupportTopic.Catalogue;
        }

        if (saysPolicy)
        {
            return SupportTopic.Policy;
        }

        if (saysMixed)
        {
            return SupportTopic.Mixed;
        }

        return SupportTopic.Other;
    }
}
