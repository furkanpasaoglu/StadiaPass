var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("stadiapass-pgdata")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

var database = postgres.AddDatabase("stadiapassdb");

var cache = builder.AddRedis("cache")
    .WithRedisInsight()
    .WithLifetime(ContainerLifetime.Persistent);

// No data volume on purpose: the realm is re-imported on every start, so realm changes always take effect
// and the demo users are deterministic.
var keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithRealmImport("./realms")
    // All local services share the "localhost" cookie jar, so the browser can send a large cookie header.
    // Raise the limit rather than answering 431 while developing.
    .WithEnvironment("QUARKUS_HTTP_LIMITS_MAX_HEADER_SIZE", "32K");

var webApi = builder.AddProject<Projects.StadiaPass_WebAPI>("webapi")
    .WithReference(database)
    .WithReference(cache)
    .WithReference(keycloak)
    .WaitFor(database)
    .WaitFor(cache)
    .WaitFor(keycloak)
    .WithEnvironment("Keycloak__PublicAuthority", keycloak.GetEndpoint("http"))
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "/scalar/v1";
        url.DisplayText = "API Reference (Scalar)";
    });

builder.AddProject<Projects.StadiaPass_WebMVC>("webmvc")
    .WithReference(webApi)
    .WithReference(cache)
    .WithReference(keycloak)
    .WaitFor(webApi)
    .WaitFor(keycloak)
    .WithEnvironment("Keycloak__PublicAuthority", keycloak.GetEndpoint("http"))
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

await builder.Build().RunAsync();
