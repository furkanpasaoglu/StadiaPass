using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;

namespace StadiaPass.Persistence.Knowledge;

/// <summary>
/// One section of a policy document, with the vector that says what it is about.
/// </summary>
/// <remarks>
/// A persistence row, not a domain entity: it has no rules, it is written whole and read whole, and it
/// exists so that PostgreSQL can answer "which of these sits nearest to this vector" - a question that
/// would take a vector database if the corpus were large, and takes one column type while it is not.
/// </remarks>
public sealed class KnowledgeChunkRow
{
    public Guid Id { get; init; }

    public string Document { get; init; } = null!;

    public string Title { get; init; } = null!;

    public string Heading { get; init; } = null!;

    public string Text { get; init; } = null!;

    public int Position { get; init; }

    /// <summary>Fingerprint of the document the row was cut from, the same on every row of the document.</summary>
    public string ContentHash { get; init; } = null!;

    /// <summary>The embedding model that produced <see cref="Embedding"/>; rows from another are not comparable.</summary>
    public string Model { get; init; } = null!;

    public Vector Embedding { get; init; } = null!;
}

internal sealed class KnowledgeChunkRowConfiguration : IEntityTypeConfiguration<KnowledgeChunkRow>
{
    /// <summary>
    /// The width of a bge-m3 vector. Fixed in the column type, so a vector from a model of a different
    /// width is refused at the write rather than compared as if it fitted.
    /// </summary>
    public const int Dimensions = 1024;

    public void Configure(EntityTypeBuilder<KnowledgeChunkRow> builder)
    {
        builder.ToTable("knowledge_chunks");

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(row => row.Document).HasColumnName("document").HasMaxLength(120).IsRequired();
        builder.Property(row => row.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(row => row.Heading).HasColumnName("heading").HasMaxLength(200).IsRequired();
        builder.Property(row => row.Text).HasColumnName("text").IsRequired();
        builder.Property(row => row.Position).HasColumnName("position").IsRequired();
        builder.Property(row => row.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        builder.Property(row => row.Model).HasColumnName("model").HasMaxLength(80).IsRequired();

        builder.Property(row => row.Embedding)
            .HasColumnName("embedding")
            .HasColumnType($"vector({Dimensions})")
            .IsRequired();

        // Replacing a document is "delete where document = x": the index is what keeps that from being a
        // scan. No index on the vector - the corpus is a few dozen rows and a scan is the fastest thing
        // PostgreSQL can do with them; an HNSW index is for when it is not.
        builder.HasIndex(row => row.Document).HasDatabaseName("ix_knowledge_chunks_document");
    }
}
