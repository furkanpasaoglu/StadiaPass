using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Persistence.Configurations;

internal sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.ToTable("matches");

        builder.HasKey(match => match.Id);
        builder.Property(match => match.Id).ValueGeneratedNever();

        builder.Property(match => match.CategoryId).IsRequired();
        builder.Property(match => match.CategoryName).HasMaxLength(60).IsRequired();

        builder.Property(match => match.VenueId).IsRequired();
        builder.Property(match => match.VenueName).HasMaxLength(120).IsRequired();
        builder.Property(match => match.HomeTeam).HasMaxLength(80).IsRequired();
        builder.Property(match => match.AwayTeam).HasMaxLength(80).IsRequired();
        builder.Property(match => match.KickOffUtc).IsRequired();

        builder.Property(match => match.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(match => match.Capacity).IsRequired();
        builder.Property(match => match.AvailableSeatCount).IsRequired();
        builder.Property(match => match.ReservedSeatCount).IsRequired();
        builder.Property(match => match.SoldSeatCount).IsRequired();

        builder.HasIndex(match => match.KickOffUtc);
        builder.HasIndex(match => match.CategoryName);

        builder.HasOne<Domain.Categories.SportCategory>()
            .WithMany()
            .HasForeignKey(match => match.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Venues.Venue>()
            .WithMany()
            .HasForeignKey(match => match.VenueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(match => match.Seats)
            .WithOne()
            .HasForeignKey("MatchId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Match.Seats))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(match => match.DomainEvents);
    }
}

internal sealed class MatchSeatConfiguration : IEntityTypeConfiguration<MatchSeat>
{
    public void Configure(EntityTypeBuilder<MatchSeat> builder)
    {
        builder.ToTable("match_seats");

        builder.HasKey(seat => seat.Id);
        builder.Property(seat => seat.Id).ValueGeneratedNever();

        // PostgreSQL already stamps every row with the id of the transaction that last wrote it, in the
        // hidden `xmin` system column. Mapping that as the concurrency token gives optimistic locking for
        // free: no extra column, no migration, and no version field leaking into the domain entity. Every
        // UPDATE now carries `AND xmin = <value read>`, so whoever writes second matches zero rows and gets
        // a DbUpdateConcurrencyException instead of silently overwriting the winner.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(seat => seat.SeatNumber)
            .HasConversion(
                seatNumber => seatNumber.ToString(),
                value => SeatNumber.Parse(value))
            .HasColumnName("seat_number")
            .HasMaxLength(24)
            .IsRequired();

        builder.OwnsOne(seat => seat.Price, price =>
        {
            price.Property(money => money.Amount)
                .HasColumnName("price_amount")
                .HasPrecision(18, 2)
                .IsRequired();

            price.Property(money => money.Currency)
                .HasColumnName("price_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.Navigation(seat => seat.Price).IsRequired();

        builder.Property(seat => seat.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(seat => seat.HolderReference).HasMaxLength(128);

        builder.HasIndex("MatchId", nameof(MatchSeat.SeatNumber)).IsUnique().HasDatabaseName("ix_match_seats_match_seat");
        builder.HasIndex("MatchId", nameof(MatchSeat.Status));
    }
}
