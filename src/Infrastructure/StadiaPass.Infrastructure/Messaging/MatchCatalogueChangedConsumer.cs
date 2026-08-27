using MassTransit;
using MediatR;
using StadiaPass.Application.Matches.Commands.IndexMatch;
using StadiaPass.Application.Matches.Events;

namespace StadiaPass.Infrastructure.Messaging;

/// <summary>
/// Keeps the search index level with the catalogue, one fixture at a time.
/// </summary>
/// <remarks>
/// <para>
/// The far side of the outbox, and the reason the reindex command is an operational tool rather than a
/// nightly job: a match opened in the back office is findable a moment later without anybody rebuilding
/// anything.
/// </para>
/// <para>
/// The outbox delivers at least once, so this will one day see the same fixture twice. Writing a document
/// under a key that is the match's own identifier makes the second write an overwrite of the first, and the
/// command re-reads the row rather than trusting the message, so a redelivery lands the same document as the
/// original - or a fresher one, which is also correct.
/// </para>
/// </remarks>
internal sealed class MatchCatalogueChangedConsumer(ISender sender) : IConsumer<MatchCatalogueChangedEvent>
{
    public Task Consume(ConsumeContext<MatchCatalogueChangedEvent> context) =>
        sender.Send(new IndexMatchCommand(context.Message.MatchId), context.CancellationToken);
}
