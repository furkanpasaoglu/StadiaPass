using Microsoft.Extensions.AI;
using StadiaPass.AgentHost.Guardrails;

namespace StadiaPass.AgentHost.Policy;

/// <summary>
/// Retrieve, then answer: the passages nearest the question go in front of the model, and the model
/// writes from them and cites them.
/// </summary>
/// <remarks>
/// <para>
/// The chat client is the same one the analyst runs on - guardrail, telemetry and all - so a customer's
/// email in a question is masked before the model sees it, and every call is metered under the GenAI
/// conventions without a line of code here.
/// </para>
/// <para>
/// There is always a nearest passage, so retrieval alone cannot say "the documents do not cover this";
/// that judgement is the model's, reading the passages under instructions that tell it to say so. The
/// one case decided here is an empty result - a store with nothing in it - because asking a model to
/// answer from no passages is asking it to make something up.
/// </para>
/// </remarks>
internal sealed partial class PolicyAssistant(
    IPolicyRetriever retriever,
    IChatClient chatClient,
    ILogger<PolicyAssistant> logger)
{
    public const string NoPassagesAnswer = "This topic is not covered in the documents.";

    /// <summary>
    /// Three is what an answer is written from: the section that has it, and two neighbours that
    /// give the model the exception or the related rule it would otherwise miss. More than that and a
    /// passage about a nearby topic starts to look like an answer.
    /// </summary>
    private const int PassageCount = 3;

    public async Task<PolicyAnswer> AskAsync(string question, CancellationToken cancellationToken)
    {
        // Masked here, before retrieval, and not only in the chat client's guardrail: retrieval runs first,
        // and the question as typed would otherwise reach the MCP server, the API, its logs and the embedding
        // model. The rules do not depend on who is asking about them, so nothing is lost by the placeholder.
        var redaction = PersonalData.Redact(question);

        if (redaction.RemovedSomething)
        {
            PersonalDataMasked(logger, redaction.Removed.Count);
        }

        question = redaction.Text;

        var passages = await retriever.RetrieveAsync(question, cancellationToken);

        if (passages.Count is 0)
        {
            NothingRetrieved(logger);

            return new PolicyAnswer(question, NoPassagesAnswer, []);
        }

        var prompt = PolicyPrompt.Build(question, passages.Take(PassageCount).ToArray());

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            new ChatOptions
            {
                Instructions = PolicyAgent.Instructions,
                // A policy is read out, not interpreted. Determinism first, personality never.
                Temperature = 0f
            },
            cancellationToken);

        var sources = passages
            .Take(PassageCount)
            .Select((passage, index) =>
                new PolicySource(index + 1, passage.Document, passage.Title, passage.Heading, passage.Score))
            .ToArray();

        Answered(logger, sources.Length, sources[0].Document, sources[0].Heading);

        return new PolicyAnswer(question, response.Text.Trim(), sources);
    }

    [LoggerMessage(
        EventId = 9200,
        Level = LogLevel.Information,
        Message = "Policy question answered from {PassageCount} passage(s); nearest was {Document} > {Heading}")]
    private static partial void Answered(ILogger logger, int passageCount, string document, string heading);

    [LoggerMessage(
        EventId = 9202,
        Level = LogLevel.Information,
        Message = "Masked {Count} personal datum/data in a policy question before retrieval")]
    private static partial void PersonalDataMasked(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9201,
        Level = LogLevel.Warning,
        Message = "Policy question retrieved no passages at all; the knowledge store may be empty")]
    private static partial void NothingRetrieved(ILogger logger);
}
