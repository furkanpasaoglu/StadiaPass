using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace StadiaPass.AgentHost.Guardrails;

/// <summary>
/// The seam every word crosses on its way to the model and on its way back, with the personal data taken
/// out of it in both directions.
/// </summary>
/// <remarks>
/// <para>
/// It is a chat client rather than something inside the agent because of where it has to sit: outermost in
/// the pipeline, above the telemetry and above the provider. Anywhere further in and the datum has already
/// been written to a trace before it is removed from the prompt, which is a guardrail that files the
/// evidence and then closes the door.
/// </para>
/// <para>
/// Inbound it copies on write. A clean conversation - which is very nearly all of them - is handed over as
/// exactly the messages it arrived as, and only a message that actually had something removed is rebuilt,
/// so the ordinary question costs nothing and the caller's own history is never rewritten underneath it.
/// </para>
/// <para>
/// The streaming path collects the whole answer before it redacts any of it, and that is the deliberate
/// part. A model answers in fragments and an address split across two of them - <c>"ahmet"</c> then
/// <c>"@example.com"</c> - is invisible to anything reading a fragment at a time. The alternative is to
/// stream everything except a held-back tail long enough to hide a half-finished pattern; for the two- or
/// three-sentence answers this analyst gives, that tail is the whole answer, so it would be buffering with
/// extra steps and a boundary bug waiting in it. The cost is honest and small: the answer appears at once
/// instead of word by word.
/// </para>
/// </remarks>
internal sealed partial class PersonalDataGuardrail(
    IChatClient innerClient,
    GuardrailMetrics metrics,
    ILogger<PersonalDataGuardrail> logger) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(Guard(messages), options, cancellationToken);

        foreach (var message in response.Messages)
        {
            // Each content on its own is enough here: what arrives is a finished message, so a datum is
            // whole inside the text that holds it. Only a stream can cut one in half.
            if (Redacted(message.Contents, GuardrailMetrics.Outbound) is { } redacted)
            {
                message.Contents = redacted;
            }
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in base.GetStreamingResponseAsync(Guard(messages), options, cancellationToken))
        {
            updates.Add(update);
        }

        RedactAcross(updates);

        foreach (var update in updates)
        {
            yield return update;
        }
    }

    /// <summary>
    /// Redacts the answer as one piece of text, then puts it back where the first words of it were. Updates
    /// that carry no text - a call for a tool, a usage report - are not touched: the guardrail rewrites what
    /// the model said, never what it wants to do next.
    /// </summary>
    private void RedactAcross(List<ChatResponseUpdate> updates)
    {
        var spoken = string.Concat(updates
            .SelectMany(update => update.Contents)
            .OfType<TextContent>()
            .Select(text => text.Text));

        var redaction = PersonalData.Redact(spoken);

        if (!redaction.RemovedSomething)
        {
            return;
        }

        Report(redaction, GuardrailMetrics.Outbound);

        var placed = false;

        foreach (var update in updates)
        {
            if (!update.Contents.OfType<TextContent>().Any())
            {
                continue;
            }

            var kept = new List<AIContent>();

            foreach (var content in update.Contents)
            {
                if (content is not TextContent)
                {
                    kept.Add(content);

                    continue;
                }

                if (!placed)
                {
                    kept.Add(new TextContent(redaction.Text));
                    placed = true;
                }
            }

            update.Contents = kept;
        }
    }

    private List<ChatMessage> Guard(IEnumerable<ChatMessage> messages)
    {
        var guarded = new List<ChatMessage>();

        foreach (var message in messages)
        {
            if (Redacted(message.Contents, GuardrailMetrics.Inbound) is not { } redacted)
            {
                guarded.Add(message);

                continue;
            }

            guarded.Add(new ChatMessage(message.Role, redacted)
            {
                AuthorName = message.AuthorName,
                MessageId = message.MessageId,
                AdditionalProperties = message.AdditionalProperties
            });
        }

        return guarded;
    }

    /// <summary>
    /// The contents with their text redacted, or <see langword="null"/> when there was nothing to remove -
    /// which is what lets the ordinary case pass through as itself.
    /// </summary>
    /// <remarks>
    /// A tool result is read only when it came back as text, which is what an MCP tool returns. Anything
    /// structured is left exactly as it is: taking a payload apart to search it risks changing a figure,
    /// and a guardrail that quietly edits the analyst's numbers is a worse bug than the one it prevents.
    /// </remarks>
    private List<AIContent>? Redacted(IList<AIContent> contents, string direction)
    {
        List<AIContent>? redacted = null;

        for (var index = 0; index < contents.Count; index++)
        {
            var replacement = Redacted(contents[index], direction);

            if (replacement is null)
            {
                continue;
            }

            redacted ??= [.. contents];
            redacted[index] = replacement;
        }

        return redacted;
    }

    private AIContent? Redacted(AIContent content, string direction)
    {
        switch (content)
        {
            case TextContent text:
            {
                var redaction = PersonalData.Redact(text.Text);

                if (!redaction.RemovedSomething)
                {
                    return null;
                }

                Report(redaction, direction);

                return new TextContent(redaction.Text) { AdditionalProperties = text.AdditionalProperties };
            }

            case FunctionResultContent { Result: string result } function:
            {
                var redaction = PersonalData.Redact(result);

                if (!redaction.RemovedSomething)
                {
                    return null;
                }

                Report(redaction, direction);

                return new FunctionResultContent(function.CallId, redaction.Text)
                {
                    Exception = function.Exception,
                    AdditionalProperties = function.AdditionalProperties
                };
            }

            default:
                return null;
        }
    }

    private void Report(Redaction redaction, string direction)
    {
        foreach (var kind in redaction.Removed)
        {
            metrics.Record(kind, direction);
        }

        // The kinds, never the values: a log that records what it just removed has removed nothing.
        PersonalDataRemoved(logger, direction, redaction.Removed);
    }

    [LoggerMessage(
        EventId = 7500,
        Level = LogLevel.Warning,
        Message = "Personal data removed from {Direction} agent text: {Kinds}")]
    private static partial void PersonalDataRemoved(
        ILogger logger,
        string direction,
        IReadOnlyList<PersonalDataKind> kinds);
}
