using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Persistence.Configurations;

internal sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("venues");

        builder.HasKey(venue => venue.Id);
        builder.Property(venue => venue.Id).ValueGeneratedNever();

        builder.Property(venue => venue.Name).HasMaxLength(120).IsRequired();
        builder.Property(venue => venue.City).HasMaxLength(80).IsRequired();

        builder.Property(venue => venue.Kind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(venue => new { venue.Name, venue.City }).IsUnique();

        builder.HasMany(venue => venue.Blocks)
            .WithOne()
            .HasForeignKey("VenueId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Venue.Blocks))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(venue => venue.Capacity);
        builder.Ignore(venue => venue.DomainEvents);
    }
}

internal sealed class VenueBlockConfiguration : IEntityTypeConfiguration<VenueBlock>
{
    public void Configure(EntityTypeBuilder<VenueBlock> builder)
    {
        builder.ToTable("venue_blocks");

        builder.HasKey(block => block.Id);
        builder.Property(block => block.Id).ValueGeneratedNever();

        builder.Property(block => block.Name).HasMaxLength(10).IsRequired();
        builder.Property(block => block.RowCount).IsRequired();
        builder.Property(block => block.SeatsPerRow).IsRequired();
        builder.Property(block => block.PriceMultiplier).HasPrecision(6, 2).IsRequired();

        builder.Ignore(block => block.Capacity);
    }
}
