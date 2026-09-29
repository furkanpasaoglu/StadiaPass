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
    PolicyQuestionSplitter splitter,
    PolicyReranker reranker,
    IChatClient chatClient,
    ILogger<PolicyAssistant> logger)
{
    /// <summary>
    /// The same sentence the instructions ask the model for, so an empty store and an uncovered question read
    /// alike to staff - and to the evals, which look for exactly these words.
    /// </summary>
    public const string NoPassagesAnswer = "Bu konuda belgelerde bilgi yok.";

    /// <summary>
    /// Three is what an answer is written from: the section that has it, and two neighbours that
    /// give the model the exception or the related rule it would otherwise miss. More than that and a
    /// passage about a nearby topic starts to look like an answer.
    /// </summary>
    private const int PassageCount = 3;

    /// <summary>
    /// The ceiling when a message asks several rule questions: room for the best two of three questions, or
    /// the best one of up to six, without handing the model a wall of sections to wade through.
    /// </summary>
    private const int SplitPassageCount = 6;

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

        var passages = await RetrievePassagesAsync(question, cancellationToken);

        if (passages.Count is 0)
        {
            NothingRetrieved(logger);

            return new PolicyAnswer(question, NoPassagesAnswer, []);
        }

        // The model answers the question as it was asked, whatever it was split into for retrieval: the
        // splitting decides what the model reads, never what it is answering.
        var prompt = PolicyPrompt.Build(question, passages.ToArray());

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
            .Select((passage, index) =>
                new PolicySource(index + 1, passage.Document, passage.Title, passage.Heading, passage.Score))
            .ToArray();

        Answered(logger, sources.Length, sources[0].Document, sources[0].Heading);

        return new PolicyAnswer(question, response.Text.Trim(), sources);
    }

    /// <summary>
    /// One rule question: retrieve with the message as it was typed, exactly as before splitting existed.
    /// Several: retrieve for each on its own, then take the passages in turns - the best of every question
    /// first, then the second of every question - so no question is crowded out by another's neighbours.
    /// Either way, each retrieval is reranked down to three before anything is taken from it.
    /// </summary>
    private async Task<List<PolicyPassage>> RetrievePassagesAsync(string question, CancellationToken cancellationToken)
    {
        var ruleQuestions = await splitter.SplitAsync(question, cancellationToken);

        if (ruleQuestions.Count <= 1)
        {
            var found = await RetrieveAndRerankAsync(question, cancellationToken);

            return found.ToList();
        }

        SplitInto(logger, ruleQuestions.Count);

        // Side by side, not one after the other: each retrieval is a round trip to the MCP server, an
        // embedding and a rerank, and none of them depends on another.
        var retrievals = ruleQuestions.Select(ruleQuestion => RetrieveAndRerankAsync(ruleQuestion, cancellationToken));
        var results = await Task.WhenAll(retrievals);

        var passages = new List<PolicyPassage>();
        var longest = results.Max(result => result.Count);

        for (var rank = 0; rank < longest; rank++)
        {
            foreach (var result in results)
            {
                if (rank >= result.Count)
                {
                    continue;
                }

                var candidate = result[rank];
                var alreadyTaken = passages.Any(passage =>
                    passage.Document == candidate.Document && passage.Heading == candidate.Heading);

                if (alreadyTaken)
                {
                    continue;
                }

                passages.Add(candidate);

                if (passages.Count == SplitPassageCount)
                {
                    return passages;
                }
            }
        }

        return passages;
    }

    /// <summary>Wide retrieval, then the reranker's pick of the three the model will read.</summary>
    private async Task<IReadOnlyList<PolicyPassage>> RetrieveAndRerankAsync(
        string question,
        CancellationToken cancellationToken)
    {
        var candidates = await retriever.RetrieveAsync(question, cancellationToken);
        var chosen = await reranker.RerankAsync(question, candidates, PassageCount, cancellationToken);

        // Logged only when the reranker changed what the model reads, which is the only case it earned its
        // model call: a passage from beyond the vectors' own top three was brought in.
        var promoted = chosen.Count(passage => !candidates.Take(PassageCount).Contains(passage));

        if (promoted > 0)
        {
            Promoted(logger, promoted, candidates.Count);
        }

        return chosen;
    }

    [LoggerMessage(
        EventId = 9204,
        Level = LogLevel.Information,
        Message = "Reranker brought {Count} passage(s) into the three from beyond the vectors' top three, out of {Candidates} candidates")]
    private static partial void Promoted(ILogger logger, int count, int candidates);

    [LoggerMessage(
        EventId = 9203,
        Level = LogLevel.Information,
        Message = "Policy question split into {Count} rule questions, each retrieved for on its own")]
    private static partial void SplitInto(ILogger logger, int count);

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
