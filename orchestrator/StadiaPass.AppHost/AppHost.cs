var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("stadiapass-pgdata")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

var database = postgres.AddDatabase("stadiapassdb");

var cache = builder.AddRedis("cache")
    .WithRedisInsight()
    .WithLifetime(ContainerLifetime.Persistent);

var webApi = builder.AddProject<Projects.StadiaPass_WebAPI>("webapi")
    .WithReference(database)
    .WithReference(cache)
    .WaitFor(database)
    .WaitFor(cache)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "/scalar/v1";
        url.DisplayText = "API Reference (Scalar)";
    });

builder.AddProject<Projects.StadiaPass_WebMVC>("webmvc")
    .WithReference(webApi)
    .WaitFor(webApi)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

await builder.Build().RunAsync();
