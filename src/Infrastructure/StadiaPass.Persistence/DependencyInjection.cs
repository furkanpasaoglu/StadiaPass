using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Persistence.Outbox;
using StadiaPass.Persistence.Repositories;

namespace StadiaPass.Persistence;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "stadiapassdb";

    public static IHostApplicationBuilder AddPersistence(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<StadiaPassDbContext>(DatabaseConnectionName);

        builder.Services.AddHostedService<DatabaseInitializer>();
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
        builder.Services.AddScoped<IOutbox, OutboxWriter>();
        builder.Services.AddHostedService<OutboxProcessor>();
        builder.Services.AddScoped<ITicketRepository, TicketRepository>();
        builder.Services.AddScoped<IMatchRepository, MatchRepository>();
        builder.Services.AddScoped<IVenueRepository, VenueRepository>();
        builder.Services.AddScoped<ISportCategoryRepository, SportCategoryRepository>();

        return builder;
    }
}
