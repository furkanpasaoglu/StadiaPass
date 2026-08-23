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

        builder.Property(match => match.Category)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

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
        builder.HasIndex(match => match.Category);

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
