using System.Net.Http.Json;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("stadiapass-pgdata")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

var database = postgres.AddDatabase("stadiapassdb");

var cache = builder.AddRedis("cache")
    .WithRedisInsight()
    .WithLifetime(ContainerLifetime.Persistent);

var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithLifetime(ContainerLifetime.Persistent);


var searchPassword = builder.AddParameter("search-password", "stadiapass-search-dev", secret: true);

var search = builder.AddElasticsearch("search", searchPassword)
    .WithDataVolume("stadiapass-esdata")
    .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
    .WithLifetime(ContainerLifetime.Persistent);

var keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithRealmImport("./realms")
    .WithEnvironment("QUARKUS_HTTP_LIMITS_MAX_HEADER_SIZE", "32K");


const string vaultRootToken = "stadiapass-root-token";
const string vaultSecretPath = "stadiapass";

var vault = builder.AddContainer("vault", "hashicorp/vault", "1.21")
    .WithEnvironment("VAULT_DEV_ROOT_TOKEN_ID", vaultRootToken)
    .WithEnvironment("VAULT_DEV_LISTEN_ADDRESS", "0.0.0.0:8200")
    .WithArgs("server", "-dev")
    .WithHttpEndpoint(port: 8200, targetPort: 8200, name: "http")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Vault UI")
    .WithHttpHealthCheck("/v1/sys/health", endpointName: "http");

var vaultEndpoint = vault.GetEndpoint("http");

var webApi = builder.AddProject<Projects.StadiaPass_WebAPI>("webapi")
    .WithReference(database)
    .WithReference(cache)
    .WithReference(keycloak)
    .WithReference(messaging)
    .WithReference(search)
    .WaitFor(database)
    .WaitFor(cache)
    .WaitFor(keycloak)
    .WaitFor(messaging)
    .WaitFor(search)
    .WithReference(vaultEndpoint)
    .WaitFor(vault)
    .WithEnvironment("Vault__Address", vaultEndpoint)
    .WithEnvironment("Vault__Token", vaultRootToken)
    .WithEnvironment("Vault__Path", vaultSecretPath)
    .WithEnvironment("Keycloak__PublicAuthority", keycloak.GetEndpoint("http"))
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "/scalar/v1";
        url.DisplayText = "API Reference (Scalar)";
    });

var mcpServer = builder.AddProject<Projects.StadiaPass_McpServer>("mcpserver")
    .WithReference(webApi)
    .WaitFor(webApi)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithReference(vaultEndpoint)
    .WaitFor(vault)
    .WithEnvironment("Vault__Address", vaultEndpoint)
    .WithEnvironment("Vault__Token", vaultRootToken)
    .WithEnvironment("Vault__Path", vaultSecretPath)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "/mcp";
        url.DisplayText = "MCP Endpoint";
    });

var mcpEndpoint = mcpServer.GetEndpoint("http");

builder.AddProject<Projects.StadiaPass_AgentHost>("agenthost")
    .WithReference(mcpServer)
    .WaitFor(mcpServer)
    .WithEnvironment("Agent__McpEndpoint", ReferenceExpression.Create($"{mcpEndpoint}/mcp"))
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "/devui";
        url.DisplayText = "Agent DevUI";
    });

builder.AddProject<Projects.StadiaPass_WebMVC>("webmvc")
    .WithReference(webApi)
    .WithReference(cache)
    .WithReference(keycloak)
    .WaitFor(webApi)
    .WaitFor(keycloak)
    .WithReference(vaultEndpoint)
    .WaitFor(vault)
    .WithEnvironment("Vault__Address", vaultEndpoint)
    .WithEnvironment("Vault__Token", vaultRootToken)
    .WithEnvironment("Vault__Path", vaultSecretPath)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();


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


builder.Eventing.Subscribe<ResourceReadyEvent>(vault.Resource, async (@event, cancellationToken) =>
{
    var secrets = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["ConnectionStrings:stadiapassdb"] =
            await database.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken),
        ["ConnectionStrings:cache"] =
            await cache.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken),
        ["ConnectionStrings:messaging"] =
            await messaging.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken),
        ["ConnectionStrings:search"] =
            await search.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken),
        ["Keycloak:AdminClientSecret"] = "stadiapass-admin-dev-secret",
        ["Keycloak:ClientSecret"] = "stadiapass-mvc-dev-secret",
        ["Keycloak:McpClientSecret"] = "stadiapass-mcp-dev-secret",
        ["PaymentProvider:Type"] = builder.Configuration["PaymentProvider:Type"],
        ["PaymentProvider:SecretKey"] = builder.Configuration["PaymentProvider:SecretKey"],
        ["PaymentProvider:WebhookSecret"] = builder.Configuration["PaymentProvider:WebhookSecret"],

        // Google account and a 16-character App Password, handed through from the AppHost environment the
        // same way the Stripe key is. Neither is written down in this repository.
        ["Smtp:Host"] = builder.Configuration["Smtp:Host"],
        ["Smtp:Port"] = builder.Configuration["Smtp:Port"],
        ["Smtp:SenderName"] = builder.Configuration["Smtp:SenderName"],
        ["Smtp:SenderEmail"] = builder.Configuration["Smtp:SenderEmail"],
        ["Smtp:UserName"] = builder.Configuration["Smtp:UserName"],
        ["Smtp:Password"] = builder.Configuration["Smtp:Password"]
    };

    var payload = new
    {
        data = secrets.Where(entry => entry.Value is { Length: > 0 }).ToDictionary(StringComparer.Ordinal)
    };

    using var client = new HttpClient { BaseAddress = new Uri(vaultEndpoint.Url) };
    client.DefaultRequestHeaders.Add("X-Vault-Token", vaultRootToken);

    using var response = await client.PostAsJsonAsync(
        $"/v1/secret/data/{vaultSecretPath}", payload, cancellationToken);

    response.EnsureSuccessStatusCode();
});


await builder.Build().RunAsync();
