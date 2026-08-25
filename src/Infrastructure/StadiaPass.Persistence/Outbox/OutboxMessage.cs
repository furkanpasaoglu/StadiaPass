using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StadiaPass.Persistence.Outbox;

/// <summary>
/// A message that has been decided but not yet sent. It is written in the same transaction as the work that
/// caused it, so it exists if and only if that work committed.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    public DateTimeOffset OccurredOnUtc { get; init; }

    /// <summary>The full type name, which is what says how to read <see cref="Content"/> back.</summary>
    public string Type { get; init; } = null!;

    public string Content { get; init; } = null!;

    /// <summary>Null until the broker has taken it. This column is the queue.</summary>
    public DateTimeOffset? ProcessedOnUtc { get; set; }

    /// <summary>Why the last attempt did not work, kept so a stuck message can be explained.</summary>
    public string? Error { get; set; }

    /// <summary>How many times the broker has been asked to take it.</summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Set once the sweeper has given up. A message that can never be delivered would otherwise be retried
    /// every five seconds for as long as the process lives, writing a log line each time and taking a slot
    /// in every batch. Nothing clears this on its own: a row with a time in here is waiting for a person.
    /// </summary>
    public DateTimeOffset? FailedOnUtc { get; set; }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(message => message.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();
        builder.Property(message => message.Type).HasColumnName("type").HasMaxLength(300).IsRequired();
        builder.Property(message => message.Content).HasColumnName("content").IsRequired();
        builder.Property(message => message.ProcessedOnUtc).HasColumnName("processed_on_utc");
        builder.Property(message => message.Error).HasColumnName("error");
        builder.Property(message => message.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(message => message.FailedOnUtc).HasColumnName("failed_on_utc");

        // Partial index: the worker only ever asks for the unsent ones, and once a table has months of sent
        // messages in it an index over all of them is mostly pages it will never read.
        builder.HasIndex(message => message.OccurredOnUtc)
            .HasDatabaseName("ix_outbox_messages_unprocessed")
            .HasFilter("processed_on_utc IS NULL");

        // The dead count is measured every five seconds, and without this it is a parallel sequential scan of
        // a table that keeps thirty days of delivered messages - so the cheapest thing the sweeper does turns
        // into the most expensive one exactly when the table is busiest. Measured on 200k rows: 5.9ms against
        // 0.03ms. Partial again, and tiny: a healthy system has nothing in here at all.
        builder.HasIndex(message => message.FailedOnUtc)
            .HasDatabaseName("ix_outbox_messages_dead")
            .HasFilter("failed_on_utc IS NOT NULL");
    }
}
