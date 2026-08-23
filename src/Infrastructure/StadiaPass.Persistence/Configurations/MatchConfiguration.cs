using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Persistence.Configurations;

internal sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.ToTable("matches");

        builder.HasKey(match => match.Id);
        builder.Property(match => match.Id).ValueGeneratedNever();

        builder.Property(match => match.HomeTeam).HasMaxLength(80).IsRequired();
        builder.Property(match => match.AwayTeam).HasMaxLength(80).IsRequired();
        builder.Property(match => match.Stadium).HasMaxLength(120).IsRequired();
        builder.Property(match => match.KickOffUtc).IsRequired();
        builder.Property(match => match.Capacity).IsRequired();
        builder.Property(match => match.IssuedTicketCount).IsRequired();

        builder.Property(match => match.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(match => match.KickOffUtc);

        builder.Ignore(match => match.RemainingCapacity);
        builder.Ignore(match => match.DomainEvents);
    }
}
