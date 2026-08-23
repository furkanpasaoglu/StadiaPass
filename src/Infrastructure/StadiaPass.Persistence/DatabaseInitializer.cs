using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StadiaPass.Domain.Matches;

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

        if (await context.Matches.AnyAsync(stoppingToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        var derby = Match.Schedule("Fenerbahce", "Galatasaray", "Sukru Saracoglu", now.AddDays(21), 50_000, now);
        var cupTie = Match.Schedule("Besiktas", "Trabzonspor", "Tupras Stadyumu", now.AddDays(28), 42_000, now);

        derby.OpenSales();
        cupTie.OpenSales();

        await context.Matches.AddRangeAsync([derby, cupTie], stoppingToken);
        await context.SaveChangesAsync(stoppingToken);

        SeedCompleted(logger, 2);
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "StadiaPass database schema is ready")]
    private static partial void SchemaReady(ILogger logger);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Seeded {MatchCount} demo matches")]
    private static partial void SeedCompleted(ILogger logger, int matchCount);
}
