using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");

        builder.HasKey(ticket => ticket.Id);
        builder.Property(ticket => ticket.Id).ValueGeneratedNever();

        builder.Property(ticket => ticket.MatchId).IsRequired();
        builder.Property(ticket => ticket.MatchSeatId).IsRequired();

        builder.Property(ticket => ticket.SeatNumber)
            .HasConversion(
                seatNumber => seatNumber.ToString(),
                value => SeatNumber.Parse(value))
            .HasColumnName("seat_number")
            .HasMaxLength(24)
            .IsRequired();

        builder.OwnsOne(ticket => ticket.Price, price =>
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

        builder.Navigation(ticket => ticket.Price).IsRequired();

        builder.Property(ticket => ticket.HolderReference).HasMaxLength(128).IsRequired();
        builder.Property(ticket => ticket.AccessCode).HasMaxLength(16).IsRequired();
        builder.Property(ticket => ticket.IssuedAtUtc).IsRequired();

        builder.Property(ticket => ticket.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(ticket => ticket.AccessCode).IsUnique();
        builder.HasIndex(ticket => ticket.HolderReference);
        builder.HasIndex(ticket => ticket.MatchSeatId);

        builder.HasOne<Domain.Matches.Match>()
            .WithMany()
            .HasForeignKey(ticket => ticket.MatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(ticket => ticket.DomainEvents);
    }
}
