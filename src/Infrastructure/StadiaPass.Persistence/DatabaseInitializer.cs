using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Persistence;

internal sealed partial class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<StadiaPassDbContext>();

        await context.Database.EnsureCreatedAsync(stoppingToken);
        SchemaReady(logger);

        if (await context.Venues.AnyAsync(stoppingToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        var stadium = Venue.Define("Sukru Saracoglu", "Istanbul", VenueKind.Stadium,
        [
            new BlockLayout("MARATON", RowCount: 10, SeatsPerRow: 15),
            new BlockLayout("KALE", RowCount: 8, SeatsPerRow: 15, PriceMultiplier: 0.75m),
            new BlockLayout("VIP", RowCount: 4, SeatsPerRow: 10, PriceMultiplier: 3m)
        ]);

        var arena = Venue.Define("Sinan Erdem Spor Salonu", "Istanbul", VenueKind.Arena,
        [
            new BlockLayout("KUZEY", RowCount: 8, SeatsPerRow: 12),
            new BlockLayout("GUNEY", RowCount: 8, SeatsPerRow: 12),
            new BlockLayout("LOCA", RowCount: 3, SeatsPerRow: 8, PriceMultiplier: 2.5m)
        ]);

        await context.Venues.AddRangeAsync([stadium, arena], stoppingToken);

        var matches = new[]
        {
            Match.Create(SportCategory.Football, stadium, "Fenerbahce", "Galatasaray",
                now.AddDays(21), Money.Create(1200m), now),
            Match.Create(SportCategory.Basketball, arena, "Anadolu Efes", "Fenerbahce Beko",
                now.AddDays(9), Money.Create(600m), now),
            Match.Create(SportCategory.Volleyball, arena, "VakifBank", "Eczacibasi",
                now.AddDays(14), Money.Create(350m), now)
        };

        await context.Matches.AddRangeAsync(matches, stoppingToken);
        await context.SaveChangesAsync(stoppingToken);

        var seatCount = matches.Sum(match => match.Capacity);
        SeedCompleted(logger, 2, matches.Length, seatCount);
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "StadiaPass database schema is ready")]
    private static partial void SchemaReady(ILogger logger);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Seeded {VenueCount} venues and {MatchCount} matches with {SeatCount} seats")]
    private static partial void SeedCompleted(ILogger logger, int venueCount, int matchCount, int seatCount);
}
