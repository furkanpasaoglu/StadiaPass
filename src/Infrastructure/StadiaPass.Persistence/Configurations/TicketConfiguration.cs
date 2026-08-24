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
        builder.Property(ticket => ticket.PaymentIntentId).HasMaxLength(128).IsRequired();
        builder.Property(ticket => ticket.IssuedAtUtc).IsRequired();

        builder.Property(ticket => ticket.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(ticket => ticket.AccessCode).IsUnique();
        builder.HasIndex(ticket => ticket.HolderReference);

        // How a webhook finds its way back to a ticket: an event arrives carrying nothing but the charge.
        builder.HasIndex(ticket => ticket.PaymentIntentId);
        // The seat may only ever carry one live ticket. Filtered rather than a plain unique index, because
        // a cancelled ticket is history and not a claim on the seat: the seat goes back to Available, gets
        // sold again, and the new ticket would collide with the old one under a blanket constraint. The
        // filter says the same thing the repository already says when it looks a seat up.
        builder.HasIndex(ticket => ticket.MatchSeatId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Issued'")
            .HasDatabaseName("ix_tickets_match_seat_issued");

        builder.HasOne<Domain.Matches.Match>()
            .WithMany()
            .HasForeignKey(ticket => ticket.MatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(ticket => ticket.DomainEvents);
    }
}
