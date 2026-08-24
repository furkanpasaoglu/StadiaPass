using Microsoft.EntityFrameworkCore;
using StadiaPass.Domain.Categories;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;
using StadiaPass.Domain.Venues;
using StadiaPass.Persistence.Outbox;

namespace StadiaPass.Persistence;

public sealed class StadiaPassDbContext(DbContextOptions<StadiaPassDbContext> options) : DbContext(options)
{
    public const string Schema = "stadiapass";

    public DbSet<SportCategory> SportCategories => Set<SportCategory>();

    public DbSet<Venue> Venues => Set<Venue>();

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<MatchSeat> MatchSeats => Set<MatchSeat>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StadiaPassDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
