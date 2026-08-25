using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StadiaPass.Domain.Categories;
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
        await ApplySchemaStopgapsAsync(context, stoppingToken);
        SchemaReady(logger);

        if (await context.SportCategories.AnyAsync(stoppingToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        var football = SportCategory.Define("Football", "Eleven a side on grass", [VenueKind.Stadium]);
        var basketball = SportCategory.Define("Basketball", "Indoor court", [VenueKind.Arena, VenueKind.Hall]);
        var volleyball = SportCategory.Define("Volleyball", "Indoor court", [VenueKind.Arena, VenueKind.Hall]);
        var handball = SportCategory.Define("Handball", "Indoor court", [VenueKind.Arena, VenueKind.Hall]);

        await context.SportCategories.AddRangeAsync([football, basketball, volleyball, handball], stoppingToken);

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
            Match.Create(football, stadium, "Fenerbahce", "Galatasaray",
                now.AddDays(21), Money.Create(1200m), now),
            Match.Create(basketball, arena, "Anadolu Efes", "Fenerbahce Beko",
                now.AddDays(9), Money.Create(600m), now),
            Match.Create(volleyball, arena, "VakifBank", "Eczacibasi",
                now.AddDays(14), Money.Create(350m), now)
        };

        await context.Matches.AddRangeAsync(matches, stoppingToken);
        await context.SaveChangesAsync(stoppingToken);

        var seatCount = matches.Sum(match => match.Capacity);
        SeedCompleted(logger, 4, 2, matches.Length, seatCount);
    }

    /// <summary>
    /// Every schema change made since the first run, said again in SQL. EnsureCreated builds the schema once
    /// and never looks at it again, so a table, a column or an index added to the model afterwards simply
    /// never appears on a database that already exists. Each statement here is written to be harmless on a
    /// fresh database too, where EnsureCreated will have built the same thing a moment ago.
    /// <para>
    /// This is a stopgap and reads like one. It is the price of not having migrations, it grows every time
    /// the schema moves, and adding migrations is what deletes the whole method.
    /// </para>
    /// </summary>
    private static async Task ApplySchemaStopgapsAsync(
        StadiaPassDbContext context,
        CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlRawAsync(
            $"""
             CREATE TABLE IF NOT EXISTS {StadiaPassDbContext.Schema}.outbox_messages (
                 id uuid NOT NULL,
                 occurred_on_utc timestamp with time zone NOT NULL,
                 type character varying(300) NOT NULL,
                 content text NOT NULL,
                 processed_on_utc timestamp with time zone NULL,
                 error text NULL,
                 attempts integer NOT NULL DEFAULT 0,
                 failed_on_utc timestamp with time zone NULL,
                 CONSTRAINT pk_outbox_messages PRIMARY KEY (id)
             );

             -- CREATE TABLE IF NOT EXISTS is a no-op on a table that already exists, so columns added after
             -- the first run need saying separately. Same stopgap, same reason: no migrations yet.
             ALTER TABLE {StadiaPassDbContext.Schema}.outbox_messages
                 ADD COLUMN IF NOT EXISTS attempts integer NOT NULL DEFAULT 0;
             ALTER TABLE {StadiaPassDbContext.Schema}.outbox_messages
                 ADD COLUMN IF NOT EXISTS failed_on_utc timestamp with time zone NULL;

             CREATE INDEX IF NOT EXISTS ix_outbox_messages_unprocessed
                 ON {StadiaPassDbContext.Schema}.outbox_messages (occurred_on_utc)
                 WHERE processed_on_utc IS NULL;

             -- A ticket now records the charge that paid for it: without it a webhook arriving weeks later
             -- carries a payment id and nothing to match it against.
             ALTER TABLE {StadiaPassDbContext.Schema}.tickets
                 ADD COLUMN IF NOT EXISTS "PaymentIntentId" character varying(128) NOT NULL DEFAULT '';

             CREATE INDEX IF NOT EXISTS "IX_tickets_PaymentIntentId"
                 ON {StadiaPassDbContext.Schema}.tickets ("PaymentIntentId");

             -- One live ticket per seat, at the level that cannot be talked out of it. Filtered on Issued
             -- because a cancelled ticket is history: the seat goes back on sale and the next buyer needs a
             -- ticket of their own. The plain index EF used to create is dropped so a database that has been
             -- around a while ends up with exactly what a fresh one gets.
             DROP INDEX IF EXISTS {StadiaPassDbContext.Schema}."IX_tickets_MatchSeatId";

             CREATE UNIQUE INDEX IF NOT EXISTS ix_tickets_match_seat_issued
                 ON {StadiaPassDbContext.Schema}.tickets ("MatchSeatId")
                 WHERE "Status" = 'Issued';

             -- The inbox: what a provider told us, written down before anything is done about it. The unique
             -- index on the provider event id is what makes a redelivery a no-op instead of a second refund.
             CREATE TABLE IF NOT EXISTS {StadiaPassDbContext.Schema}.inbox_messages (
                 id uuid NOT NULL,
                 provider_event_id character varying(128) NOT NULL,
                 provider_event_type character varying(120) NOT NULL,
                 type character varying(300) NOT NULL,
                 payload text NOT NULL,
                 received_on_utc timestamp with time zone NOT NULL,
                 processed_on_utc timestamp with time zone NULL,
                 error text NULL,
                 attempts integer NOT NULL DEFAULT 0,
                 failed_on_utc timestamp with time zone NULL,
                 CONSTRAINT pk_inbox_messages PRIMARY KEY (id)
             );

             CREATE UNIQUE INDEX IF NOT EXISTS ix_inbox_messages_provider_event
                 ON {StadiaPassDbContext.Schema}.inbox_messages (provider_event_id);

             CREATE INDEX IF NOT EXISTS ix_inbox_messages_unprocessed
                 ON {StadiaPassDbContext.Schema}.inbox_messages (received_on_utc)
                 WHERE processed_on_utc IS NULL;

             -- Both sweepers count their dead messages every five seconds for the gauges. Unindexed, that is
             -- a parallel sequential scan of a table holding thirty days of delivered messages - measured at
             -- 5.9ms against 0.03ms on 200k rows, which is the cheapest thing either worker does turning
             -- into the most expensive one exactly when the table is busiest. Partial, so a healthy system
             -- carries almost nothing in them.
             CREATE INDEX IF NOT EXISTS ix_outbox_messages_dead
                 ON {StadiaPassDbContext.Schema}.outbox_messages (failed_on_utc)
                 WHERE failed_on_utc IS NOT NULL;

             CREATE INDEX IF NOT EXISTS ix_inbox_messages_dead
                 ON {StadiaPassDbContext.Schema}.inbox_messages (failed_on_utc)
                 WHERE failed_on_utc IS NOT NULL;

             -- The expired-hold sweeper asks the whole seat table every minute and has no match to narrow by,
             -- so the (MatchId, Status) index can only be scanned end to end on its second column with the
             -- expiry left to a filter - every live hold read to find the few that have lapsed. Filtered on
             -- Reserved because nothing else can be an expired hold.
             CREATE INDEX IF NOT EXISTS ix_match_seats_expiring
                 ON {StadiaPassDbContext.Schema}.match_seats ("ReservationExpiresAtUtc")
                 WHERE "Status" = 'Reserved';
             """,
            cancellationToken);

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "StadiaPass database schema is ready")]
    private static partial void SchemaReady(ILogger logger);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Seeded {CategoryCount} categories, {VenueCount} venues and {MatchCount} matches with {SeatCount} seats")]
    private static partial void SeedCompleted(
        ILogger logger,
        int categoryCount,
        int venueCount,
        int matchCount,
        int seatCount);
}
