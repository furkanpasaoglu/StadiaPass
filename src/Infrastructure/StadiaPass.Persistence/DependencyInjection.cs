using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Domain.Abstractions;
using Pgvector.EntityFrameworkCore;
using StadiaPass.Application.Knowledge;
using StadiaPass.Persistence.Inbox;
using StadiaPass.Persistence.Knowledge;
using StadiaPass.Persistence.Matches;
using StadiaPass.Persistence.Outbox;
using StadiaPass.Persistence.Repositories;

namespace StadiaPass.Persistence;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "stadiapassdb";

    public static IHostApplicationBuilder AddPersistence(this IHostApplicationBuilder builder)
    {
        // UseVector teaches the provider the pgvector column type and the <=> operator; without it the
        // Vector property is an unknown CLR type at the first write.
        builder.AddNpgsqlDbContext<StadiaPassDbContext>(
            DatabaseConnectionName,
            configureDbContextOptions: options => options.UseNpgsql(npgsql => npgsql.UseVector()));

        builder.Services.AddHostedService<DatabaseInitializer>();
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
        builder.Services.AddScoped<IOutbox, OutboxWriter>();
        builder.Services.AddScoped<IInbox, InboxWriter>();
        builder.Services.AddScoped<IRefundLedger, RefundLedger>();
        builder.Services.AddSingleton<OutboxMetrics>();
        builder.Services.AddSingleton<InboxMetrics>();
        builder.Services.AddHostedService<OutboxProcessor>();
        builder.Services.AddHostedService<ExpiredReservationCleanupWorker>();
        builder.Services.AddHostedService<InboxProcessor>();
        builder.Services.AddScoped<ITicketRepository, TicketRepository>();
        builder.Services.AddScoped<IMatchRepository, MatchRepository>();
        builder.Services.AddScoped<IVenueRepository, VenueRepository>();
        builder.Services.AddScoped<ISportCategoryRepository, SportCategoryRepository>();
        builder.Services.AddScoped<IKnowledgeStore, KnowledgeStore>();

        return builder;
    }
}
