using Microsoft.Agents.AI.Workflows;
using StadiaPass.AgentHost.Guardrails;
using StadiaPass.AgentHost.Policy;

namespace StadiaPass.AgentHost.Support;

/// <summary>
/// One door for every staff question. The router reads it, and the workflow sends it to the analyst, the
/// policy assistant, both at once, or answers it here.
/// </summary>
/// <remarks>
/// <para>
/// A Microsoft Agent Framework workflow: four steps (executors) joined by a switch. The switch is the whole
/// idea. A question about a match goes one way, a question about a rule the other, and a question that asks
/// both goes <b>both</b> ways - the two assistants run in the same step, side by side, and each adds its
/// reply. Sending a mixed question to either one alone answers half of it and says nothing about the rest.
/// </para>
/// <para>
/// Personal data is masked once, here, before the router reads anything. Every step after that sees the
/// masked question, and it is the masked question that comes back to the caller.
/// </para>
/// <para>
/// The workflow is built per question. It is cheap - four small objects - and it keeps two questions that
/// arrive at the same moment from sharing anything.
/// </para>
/// </remarks>
internal sealed class SupportDesk(
    SupportRouter router,
    IAnalyst analyst,
    PolicyAssistant policyAssistant,
    SupportMerger merger)
{
    public const string AnalystName = "analyst";

    public const string PolicyName = "policy";

    public const string DeskName = "desk";

    public const string OtherAnswer =
        "Bu konuda yardımcı olamıyorum. Maçlar, koltuklar, satışlar ya da bilet kuralları hakkında sorabilirsiniz.";

    public async Task<SupportAnswer> AskAsync(string question, CancellationToken cancellationToken)
    {
        var masked = PersonalData.Redact(question).Text;

        var workflow = BuildWorkflow();

        await using var run = await InProcessExecution.RunAsync(
            workflow,
            masked,
            sessionId: null,
            cancellationToken: cancellationToken);

        ThrowIfAStepFailed(run);

        var replies = new List<SupportReply>();

        foreach (var output in run.OutgoingEvents.OfType<WorkflowOutputEvent>())
        {
            if (output.Data is SupportReply reply)
            {
                replies.Add(reply);
            }
        }

        // The two assistants finish in whatever order they finish; the caller always reads the facts first
        // and the rule second.
        replies = replies.OrderBy(reply => OrderOf(reply.Assistant)).ToList();

        var topic = TopicOf(replies);
        var answer = await AnswerFromAsync(masked, topic, replies, cancellationToken);
        var sources = replies.SelectMany(reply => reply.Sources).ToList();

        return new SupportAnswer(masked, topic, answer, sources, replies);
    }

    /// <summary>
    /// One reply is the answer as it stands. Two replies - a mixed question - are merged into one, which
    /// costs a model call, so it is only paid when there is something to merge.
    /// </summary>
    private async Task<string> AnswerFromAsync(
        string question,
        string topic,
        List<SupportReply> replies,
        CancellationToken cancellationToken)
    {
        if (topic != "mixed")
        {
            return replies.Count > 0 ? replies[0].Answer : OtherAnswer;
        }

        var analystReply = replies.First(reply => reply.Assistant == AnalystName).Answer;
        var policyReply = replies.First(reply => reply.Assistant == PolicyName).Answer;

        return await merger.MergeAsync(question, analystReply, policyReply, cancellationToken);
    }

    private Workflow BuildWorkflow()
    {
        var route = ExecutorBindingExtensions.BindAsExecutor<string, RoutedQuestion>(RouteAsync, "route");
        var askAnalyst = ExecutorBindingExtensions.BindAsExecutor<RoutedQuestion, SupportReply>(AskAnalystAsync, AnalystName);
        var askPolicy = ExecutorBindingExtensions.BindAsExecutor<RoutedQuestion, SupportReply>(AskPolicyAsync, PolicyName);
        var answerHere = ExecutorBindingExtensions.BindAsExecutor<RoutedQuestion, SupportReply>(AnswerHere, DeskName);

        return new WorkflowBuilder(route)
            .AddSwitch(route, cases => cases
                .AddCase((RoutedQuestion? q) => q?.Topic == SupportTopic.Catalogue, [askAnalyst])
                .AddCase((RoutedQuestion? q) => q?.Topic == SupportTopic.Policy, [askPolicy])
                .AddCase((RoutedQuestion? q) => q?.Topic == SupportTopic.Mixed, [askAnalyst, askPolicy])
                .WithDefault([answerHere]))
            .WithOutputFrom(askAnalyst, askPolicy, answerHere)
            .Build();
    }

    private async ValueTask<RoutedQuestion> RouteAsync(string question, CancellationToken cancellationToken)
    {
        var topic = await router.RouteAsync(question, cancellationToken);

        return new RoutedQuestion(question, topic);
    }

    private async ValueTask<SupportReply> AskAnalystAsync(RoutedQuestion routed, CancellationToken cancellationToken)
    {
        var answer = await analyst.AskAsync(routed.Question, cancellationToken);

        return new SupportReply(AnalystName, answer, []);
    }

    private async ValueTask<SupportReply> AskPolicyAsync(RoutedQuestion routed, CancellationToken cancellationToken)
    {
        var answer = await policyAssistant.AskAsync(routed.Question, cancellationToken);

        return new SupportReply(PolicyName, answer.Answer, answer.Sources);
    }

    private static SupportReply AnswerHere(RoutedQuestion routed) =>
        new(DeskName, OtherAnswer, []);

    /// <summary>
    /// A step that throws does not throw out of the run; the run records it as an event and carries on.
    /// Left unchecked, a failed assistant would look like a question nobody answered.
    /// </summary>
    private static void ThrowIfAStepFailed(Run run)
    {
        foreach (var failure in run.OutgoingEvents.OfType<WorkflowErrorEvent>())
        {
            throw new InvalidOperationException("The support workflow failed.", failure.Exception);
        }

        foreach (var failure in run.OutgoingEvents.OfType<ExecutorFailedEvent>())
        {
            throw new InvalidOperationException(
                $"The support workflow step '{failure.ExecutorId}' failed.",
                failure.Data as Exception);
        }
    }

    private static int OrderOf(string assistant)
    {
        if (assistant == AnalystName)
        {
            return 0;
        }

        if (assistant == PolicyName)
        {
            return 1;
        }

        return 2;
    }

    private static string TopicOf(List<SupportReply> replies)
    {
        var fromAnalyst = replies.Any(reply => reply.Assistant == AnalystName);
        var fromPolicy = replies.Any(reply => reply.Assistant == PolicyName);

        if (fromAnalyst && fromPolicy)
        {
            return "mixed";
        }

        if (fromAnalyst)
        {
            return "catalogue";
        }

        if (fromPolicy)
        {
            return "policy";
        }

        return "other";
    }
}
