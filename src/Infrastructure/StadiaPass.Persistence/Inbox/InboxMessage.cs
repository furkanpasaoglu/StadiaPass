using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StadiaPass.Persistence.Inbox;

/// <summary>
/// Something a provider told us, written down before anything is done about it.
/// </summary>
/// <remarks>
/// The mirror of the outbox, and the reason it is a separate table rather than a reuse of that one. An
/// outbox row is a consequence of a local transaction and shares its fate; an inbox row arrives from
/// outside with no transaction of ours behind it. What it needs that an outbox row does not is
/// <see cref="ProviderEventId"/>: Stripe redelivers an event for up to three days, so the same
/// <c>evt_...</c> will arrive more than once, and a unique index on it turns "have I already seen this?"
/// into a fact the database settles rather than something every consumer has to remember to ask.
/// </remarks>
public sealed class InboxMessage
{
    public Guid Id { get; init; }

    /// <summary>The provider's own id for the event. Unique: this is what makes a redelivery a no-op.</summary>
    public string ProviderEventId { get; init; } = null!;

    /// <summary>The provider's own name for it, e.g. <c>payment_intent.succeeded</c>. Kept for the trail.</summary>
    public string ProviderEventType { get; init; } = null!;

    /// <summary>
    /// Our integration event, by full type name. The provider's shape is translated at the edge - where the
    /// provider's SDK lives - so the worker below reads exactly what the outbox worker reads and nothing
    /// downstream of here has ever heard of Stripe.
    /// </summary>
    public string Type { get; init; } = null!;

    /// <summary>The translated event as JSON, read back by the same serializer that wrote it.</summary>
    public string Payload { get; init; } = null!;

    public DateTimeOffset ReceivedOnUtc { get; init; }

    /// <summary>Null until it has been handed to the broker. This column is the queue.</summary>
    public DateTimeOffset? ProcessedOnUtc { get; set; }

    public string? Error { get; set; }

    public int Attempts { get; set; }

    /// <summary>Set once the sweeper has given up. A row with a time in here is waiting for a person.</summary>
    public DateTimeOffset? FailedOnUtc { get; set; }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(message => message.ProviderEventId)
            .HasColumnName("provider_event_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(message => message.ProviderEventType)
            .HasColumnName("provider_event_type")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(message => message.Type).HasColumnName("type").HasMaxLength(300).IsRequired();
        builder.Property(message => message.Payload).HasColumnName("payload").IsRequired();
        builder.Property(message => message.ReceivedOnUtc).HasColumnName("received_on_utc").IsRequired();
        builder.Property(message => message.ProcessedOnUtc).HasColumnName("processed_on_utc");
        builder.Property(message => message.Error).HasColumnName("error");
        builder.Property(message => message.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(message => message.FailedOnUtc).HasColumnName("failed_on_utc");

        // The whole point of the table. A second delivery of the same event fails to insert, and the endpoint
        // answers the provider with the 200 it is waiting for rather than doing the work twice.
        builder.HasIndex(message => message.ProviderEventId)
            .IsUnique()
            .HasDatabaseName("ix_inbox_messages_provider_event");

        builder.HasIndex(message => message.ReceivedOnUtc)
            .HasDatabaseName("ix_inbox_messages_unprocessed")
            .HasFilter("processed_on_utc IS NULL");

        // Counted every five seconds for the dead-message gauge, and partial for the same reason the
        // outbox's is: a healthy system has nothing in here, so the index costs almost nothing and saves a
        // scan of every provider event ever received.
        builder.HasIndex(message => message.FailedOnUtc)
            .HasDatabaseName("ix_inbox_messages_dead")
            .HasFilter("failed_on_utc IS NOT NULL");
    }
}
