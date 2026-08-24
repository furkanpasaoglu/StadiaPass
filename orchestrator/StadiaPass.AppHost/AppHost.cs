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

// The payment provider is chosen outside the repository. Nothing about it is committed: the values come
// from the AppHost's own configuration - an environment variable today, a secrets manager later - and are
// forwarded to the API only when they are actually set, so a clone with no configuration still runs on the
// mock provider.
foreach (var setting in (string[])["Type", "SecretKey"])
{
    if (builder.Configuration[$"PaymentProvider:{setting}"] is { Length: > 0 } value)
    {
        webApi.WithEnvironment($"PaymentProvider__{setting}", value);
    }
}

builder.AddProject<Projects.StadiaPass_WebMVC>("webmvc")
    .WithReference(webApi)
    .WithReference(cache)
    .WithReference(keycloak)
    .WaitFor(webApi)
    .WaitFor(keycloak)
    .WithEnvironment("Keycloak__PublicAuthority", keycloak.GetEndpoint("http"))
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Metrics stack. The applications publish an OpenTelemetry scrape endpoint through ServiceDefaults;
// Prometheus pulls it and Grafana reads Prometheus, both provisioned from files so a clone comes up with
// the data source and the dashboard already in place.
var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.6.0")
    .WithBindMount("./monitoring/prometheus", "/etc/prometheus", isReadOnly: true)
    .WithArgs(
        "--config.file=/etc/prometheus/prometheus.yml",
        "--storage.tsdb.path=/prometheus",
        "--web.enable-lifecycle")
    .WithHttpEndpoint(port: 9090, targetPort: 9090, name: "http")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Prometheus");

builder.AddContainer("grafana", "grafana/grafana", "12.2.0")
    .WithBindMount("./monitoring/grafana/provisioning", "/etc/grafana/provisioning", isReadOnly: true)
    .WithBindMount("./monitoring/grafana/dashboards", "/var/lib/grafana/dashboards", isReadOnly: true)
    .WithEnvironment("GF_SECURITY_ADMIN_USER", "admin")
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", "admin")
    // Local development only: skip the login wall so the dashboard opens straight from the Aspire page.
    .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
    .WithEnvironment("GF_USERS_DEFAULT_THEME", "light")
    .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "http")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Grafana")
    .WaitFor(prometheus);

await builder.Build().RunAsync();
