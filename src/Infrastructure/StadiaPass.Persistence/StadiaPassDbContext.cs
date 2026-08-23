using Microsoft.EntityFrameworkCore;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Persistence;

public sealed class StadiaPassDbContext(DbContextOptions<StadiaPassDbContext> options) : DbContext(options)
{
    public const string Schema = "stadiapass";

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StadiaPassDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
