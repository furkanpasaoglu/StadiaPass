# StadiaPass

**English** · [Türkçe](README.tr.md)

Stadium and arena ticketing built as a reference-grade **.NET 10 / C# 14** Clean Architecture solution:
Minimal API backend, MVC front end, DDD domain model, CQRS with MediatR, Keycloak-backed dynamic permission
authorization, and .NET Aspire orchestration.

Matches belong to a sport category and are opened against a venue seating plan. Customers pick a seat off an
interactive map, the seat is held for them, and the purchase turns that hold into a ticket.

Most of what is interesting here is in that last sentence. Two people want the same seat, money moves at a
company on the other side of the internet, and the things that happen afterwards must not be able to fail the
checkout — so the seat is guarded by [optimistic concurrency](#concurrency), the charge is
[compensated when the sale does not land](#when-the-money-moved-and-the-sale-did-not), and the announcement
leaves through a [transactional outbox](#messaging).

## Architecture

```
StadiaPass.slnx
├── Directory.Build.props            # shared MSBuild settings (net10.0, nullable, warnings-as-errors)
├── Directory.Packages.props         # Central Package Management - single source of truth for versions
├── src
│   ├── Shared
│   │   ├── StadiaPass.SharedKernel  # permission vocabulary, no framework dependency
│   │   │   └── Authorization        # StadiaPassPermissions + catalogue, KeycloakRoleReader
│   │   └── StadiaPass.SharedKernel.AspNetCore  # dynamic policy provider + claims transformation
│   ├── Core
│   │   ├── StadiaPass.Domain        # aggregates, value objects, domain events (no infrastructure deps)
│   │   │   ├── Abstractions         # IRepository, IVenueRepository, IMatchRepository, ITicketRepository, IUnitOfWork
│   │   │   ├── Common               # Entity, AggregateRoot, DomainEvent, DomainException
│   │   │   │   └── ValueObjects     # Money, SeatNumber
│   │   │   ├── Categories           # SportCategory aggregate (which venue kinds it can be played in)
│   │   │   ├── Venues               # Venue aggregate + VenueBlock + VenueKind (the seating plan)
│   │   │   ├── Matches              # Match aggregate + MatchSeat + SportCategory + SeatStatus + events
│   │   │   └── Tickets              # Ticket aggregate + TicketStatus + events
│   │   └── StadiaPass.Application   # CQRS use cases (MediatR), validation, DTOs
│   │       ├── Common
│   │       │   ├── Abstractions     # IDateTimeProvider, ICacheService, ICurrentUser
│   │       │   ├── Behaviors        # LoggingBehavior, ValidationBehavior (MediatR pipeline)
│   │       │   ├── Exceptions       # NotFound, Conflict, ConcurrencyConflict, PaymentFailed, Validation
│   │       │   └── Messaging        # IntegrationEventTypes - the messages allowed on the wire
│   │       ├── Infrastructure
│   │       │   └── Abstractions     # IPaymentService, IDistributedLock, IOutbox, IEventBus
│   │       ├── Categories           # GetCategories / CreateCategory / UpdateCategory / DeleteCategory
│   │       ├── Identity             # Keycloak Admin port: Roles / Users slices
│   │       ├── Venues               # GetVenues / CreateVenue / UpdateVenue / DeleteVenue
│   │       ├── Matches              # CreateMatch / GetUpcomingMatches / GetMatchSeatMap / EventHandlers
│   │       └── Tickets              # ReserveSeat / ConfirmTicketPurchase / GetMyTickets / GetTicketById
│   ├── Infrastructure
│   │   ├── StadiaPass.Persistence   # EF Core 10 + PostgreSQL, repositories, Unit of Work, seeding
│   │   │   ├── Configurations       # IEntityTypeConfiguration per aggregate
│   │   │   ├── Outbox               # OutboxMessage + writer + the sweeper that carries it to the broker
│   │   │   └── Repositories         # Repository<T>, VenueRepository, MatchRepository, TicketRepository
│   │   └── StadiaPass.Infrastructure# adapters for the ports above
│   │       ├── Locking              # Redis SET NX PX + a Lua compare-and-delete release
│   │       ├── Messaging            # MassTransit over RabbitMQ + the TicketPurchasedEvent consumer
│   │       └── Payments             # Mock and Stripe adapters, provider strategy
│   └── Presentation
│       ├── StadiaPass.WebAPI        # Minimal API - MapGroup + IEndpoint discovery + Scalar reference
│       │   ├── Authorization        # Keycloak JWT wiring, KeycloakOptions, CurrentUser
│       │   ├── Endpoints            # VenueEndpoints, MatchEndpoints, TicketEndpoints, RoleEndpoints, UserEndpoints
│       │   └── Extensions           # GlobalExceptionHandler, OAuth2 OpenAPI transformers
│       └── StadiaPass.WebMVC        # Razor MVC UI - consumes the API over HTTP only
│           ├── Areas/Admin          # back-office: matches, venues, categories, roles, users
│           ├── Authentication       # OIDC login, KeycloakOptions, TokenBearerHandler
│           ├── Controllers          # MatchesController (seat picker), TicketsController, AccountController
│           ├── Models               # its own contracts - no reference to Domain/Application
│           └── Services             # typed HttpClients (ticketing + identity portal)
├── orchestrator
│   ├── StadiaPass.AppHost           # Aspire: PostgreSQL, Redis, RabbitMQ, Keycloak, Vault, Prometheus, Grafana
│   │   ├── monitoring               # prometheus.yml, Grafana datasource and dashboard provisioning
│   │   └── realms                   # stadiapass-realm.json - permission roles, clients, demo users
│   └── StadiaPass.ServiceDefaults   # Vault configuration, Serilog, OpenTelemetry, health checks
│       └── Logging                  # Serilog wiring, request-context enricher, credential masking
└── tests
    ├── StadiaPass.Domain.UnitTests       # aggregate invariants
    └── StadiaPass.Application.UnitTests  # vertical slice handlers with substituted ports
```

### Dependency rule

```
WebMVC ──HTTP──► WebAPI ──► Application ──► Domain
   │                │            ▲
   │                └──► Persistence / Infrastructure (implement Domain + Application abstractions)
   └──────────────► SharedKernel ◄── WebAPI          (permission contracts only)
```

`Domain` depends on nothing but `MediatR.Contracts` (marker interfaces only).
`WebMVC` never references `Domain` or `Application` — it is a pure API consumer, exactly like a third-party
client would be. The only thing it shares with the API is the permission vocabulary, which lives in
`SharedKernel` so neither side can invent a permission string of its own.

## Domain model

```
Venue (aggregate)                  Match (aggregate)                 Ticket (aggregate)
  Name, City, Kind                   Category, VenueId                 MatchId, MatchSeatId
  └── VenueBlock[]                   Capacity / seat counters          SeatNumber, Price (snapshot)
        Name, Rows, SeatsPerRow      └── MatchSeat[]                   HolderReference, AccessCode
        PriceMultiplier                    SeatNumber, Price
                                           Status: Available | Reserved | Sold
```

| Aggregate | Invariants enforced in the aggregate |
|---|---|
| `SportCategory` | at least one playable venue kind, unique name, an inactive category accepts no new match |
| `Venue` | at least one block, unique block names, plan capped at 25 000 seats, plan frozen once a match uses it |
| `Match` | teams differ, kick-off in the future (normalised to UTC), the category must be playable in the venue kind, seats materialised from the venue plan, seat counters and `SoldOut` kept consistent |
| `MatchSeat` | `Available` → `Reserve()` → `ConfirmSale()`, 10-minute hold, only the holder may buy, expired holds auto-release |
| `Ticket` | can only be issued for a seat the match has already moved to `Sold` |

Seat transitions are driven **only** through the match: `MatchSeat.Reserve/ConfirmSale/Release` are
`internal`, so `Match.ReserveSeat(seatNumber, holder, now)` and `Match.ConfirmSeatSale(...)` are the sole
entry points and the counters can never drift from the seats. Every setter is `private`; rule violations
throw `DomainRuleViolationException`, which the API maps to `422 Unprocessable Content`.

Block price multipliers are applied at match creation: with a base price of 1200 TRY, the `KALE` block
(×0.75) is 900 and `VIP` (×3) is 3600.

## Running

Requires the .NET 10 SDK and a container runtime (Docker Desktop or Podman).

```powershell
dotnet run --project orchestrator/StadiaPass.AppHost
```

Aspire starts PostgreSQL (with pgAdmin), Redis (with RedisInsight), RabbitMQ (with the management plugin),
Keycloak, Vault, the API and the MVC app. On first start the schema is created and seeded with two venues and
three matches (742 seats), and the Keycloak realm is imported.

| Resource | Default local URL |
|---|---|
| MVC UI | http://localhost:5230 |
| Keycloak | https://localhost:8080 |
| Vault UI | http://localhost:8200 |
| RabbitMQ management | shown on the `messaging` resource in the Aspire dashboard |
| API | http://localhost:5042 |
| API reference (Scalar) | http://localhost:5042/scalar/v1 |
| OpenAPI document | http://localhost:5042/openapi/v1.json |
| Health | http://localhost:5042/health |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 |

The Aspire dashboard exposes the Scalar reference as a link on the `webapi` resource. The OpenAPI document
and Scalar are mapped only in the Development environment.

## API surface

| Method | Route | Required permission |
|---|---|---|
| `GET` | `/api/v1/categories` | `StadiaPass.Categories.View` |
| `POST` | `/api/v1/categories` | `StadiaPass.Categories.Create` |
| `PUT` | `/api/v1/categories/{id}` | `StadiaPass.Categories.Update` |
| `DELETE` | `/api/v1/categories/{id}` | `StadiaPass.Categories.Delete` |
| `GET` | `/api/v1/venues` | `StadiaPass.Venues.View` |
| `POST` | `/api/v1/venues` | `StadiaPass.Venues.Create` |
| `PUT` | `/api/v1/venues/{id}` | `StadiaPass.Venues.Update` |
| `DELETE` | `/api/v1/venues/{id}` | `StadiaPass.Venues.Delete` |
| `GET` | `/api/v1/matches?category={sport}` | `StadiaPass.Matches.View` |
| `POST` | `/api/v1/matches` | `StadiaPass.Matches.Create` |
| `GET` | `/api/v1/matches/{id}/seats` | `StadiaPass.Matches.View` |
| `POST` | `/api/v1/matches/{id}/seats/{seatNumber}/reservation` | `StadiaPass.Tickets.Reserve` |
| `POST` | `/api/v1/tickets` | `StadiaPass.Tickets.Purchase` (charges the card, then issues) |
| `GET` | `/api/v1/tickets/mine` | `StadiaPass.Tickets.View` |
| `GET` | `/api/v1/tickets/{id}` | `StadiaPass.Tickets.View` (own ticket) / `StadiaPass.Tickets.ViewAll` (anybody's) |

A permission alone cannot secure `GET /api/v1/tickets/{id}`: every customer holds `Tickets.View`, because
that is what opening their own ticket takes. The handler therefore checks the holder as well, and answers
404 rather than 403 for somebody else's ticket - "forbidden" would confirm that a guessed id is real.
`Tickets.ViewAll` is what lets the box office look up the ticket in front of it.

`GET /api/v1/matches/{id}/seats` returns the map already grouped by block and row, which is exactly what a
seat picker needs to draw:

```json
{
  "blocks": [
    { "block": "VIP", "availableSeatCount": 39,
      "rows": [ { "row": 1, "seats": [
        { "seatNumber": "VIP-1-1", "number": 1, "price": 3600.00, "currency": "TRY", "status": "Available" }
      ] } ] }
  ]
}
```

Errors are returned as `ProblemDetails`: `400` validation, `404` not found, `409` conflict, `422` domain rule
violation.

## Authorization

Authentication is delegated to Keycloak; authorization is **permission-based and fully dynamic**. No role
name appears anywhere in the code.

```
Keycloak realm role  ──►  KeycloakPermissionClaimsTransformation  ──►  "permission" claim
   "StadiaPass.Tickets.Purchase"       (filtered against StadiaPassPermissions)
                                              │
        .RequireAuthorization(StadiaPassPermissions.Tickets.Purchase)
                                              │
                              PermissionPolicyProvider  ──► builds the policy on demand
                                              │
                              PermissionAuthorizationHandler  ──► 200 / 403
```

- `StadiaPassPermissions` (SharedKernel) is the only place a permission string is declared; `All` is
  discovered by reflection over its nested constants and backed by a `FrozenSet`.
- `PermissionPolicyProvider` creates an `AuthorizationPolicy` for any known permission the first time it is
  requested, so `AddPolicy(...)` never has to be written by hand.
- Roles the application does not declare are dropped by the claims transformation instead of being trusted,
  so adding a role in Keycloak cannot silently widen access.
- The MVC app runs the same transformation, so `User.HasPermission(...)` hides actions the API would refuse
  anyway — the admin menu and the seat buttons simply do not render for a customer.
- Adding a permission is a two-step change: add the constant, add the matching realm role.

### Session and token lifetime

Keycloak issues an access token good for half an hour; the session that carries it lasts hours. The MVC
app replays that token on every call to the API, so the two have to be kept in step or the user would go
on looking signed in while everything they click came back 401. `TokenRefreshingCookieEvents` validates
the cookie on each request and, two minutes before the token expires, exchanges the refresh token for a
fresh one; the renewed ticket is written back to Redis. If Keycloak refuses - the refresh token is spent,
revoked, or the session ended on its side - the local session is ended too rather than left half alive.

### Admin versus customer

| | Admin (`Matches.Create`, `Venues.*`) | Customer (`Tickets.Reserve`, `Tickets.Purchase`) |
|---|---|---|
| MVC | `/Admin/Match/Create` form, "Create match" in the nav | match list, seat picker, my tickets |
| API | define venues, create matches | read seat maps, hold a seat, buy it |

### Testing from Scalar

The OpenAPI document publishes an OAuth2 authorization code flow, so the Scalar reference renders an
**Authorize** button that redirects to Keycloak (PKCE `S256`, public client `stadiapass-scalar`) and injects
the token into every request. Each secured operation is annotated with the permission it needs.

### Roles and demo users (realm import, development only)

Business roles ship with the realm as composite roles, so a fresh start already has a working permission
matrix. Editing them in the portal changes Keycloak, not the realm file.

| Role | Permissions |
|---|---|
| `Administrator` | everything, including the identity portal |
| `MatchManager` | venues, match creation and postponement, ticket read |
| `BoxOffice` | match read, ticket read including other people's, hold, buy and cancel |
| `Customer` | match read, ticket read, hold and buy |
| `Viewer` | match and ticket read only |

| User | Password | Role |
|---|---|---|
| `mudur` | `mudur` | `Administrator` |
| `organizator` | `organizator` | `MatchManager` |
| `gise` | `gise` | `BoxOffice` |
| `musteri` | `musteri` | `Customer` |
| `seyirci` | `seyirci` | `Viewer` |

Assigning one composite role instead of a dozen individual permission roles also keeps the issued tokens
small, which matters because everything here shares the `localhost` cookie jar.

Keycloak runs at https://localhost:8080 and the realm is re-imported on every start (no data volume), so the
realm file is the source of truth. The MVC client secret in `stadiapass-realm.json` is a local development
value — replace it with a real secret store before any deployment.

## Public browsing

Browsing is anonymous, the way a ticketing site works. A visitor lands on the fixtures, opens a match and
watches the seat map fill up without an account; only holding or buying a seat asks for a sign-in.

| Reached by | Anonymous | Signed in |
|---|---|---|
| `GET /api/v1/matches` | yes | yes |
| `GET /api/v1/matches/{id}/seats` | yes | yes |
| seat reservation, purchase, back office | no | with the matching permission |

Clicking a free seat as a guest never reaches the API. The seat renders as a plain button and the page
turns the click into a sign-in round trip whose return URL carries the seat:

```
/Matches/SeatSelection/{id}              guest clicks seat GUNEY-1-1
  -> /Account/Login?returnUrl=/Matches/SeatSelection/{id}?seat=GUNEY-1-1
  -> Keycloak sign-in
  -> back to the same seat map, which offers "Hold this seat GUNEY-1-1"
```

The navigation shows **Log in** and **Register** to a guest and the signed-in username otherwise. Register
retargets the OIDC challenge at Keycloak's registration endpoint, so the handler keeps ownership of state,
nonce and PKCE. New accounts land in the realm default group, which carries the `Customer` role, so someone
who just signed up can buy immediately.

## Identity portal

Roles and users are **not** stored in the StadiaPass database. The API brokers every change to the Keycloak
Admin REST API through `IKeycloakAdminService`, using a service account confined to the `stadiapass` realm -
the master realm admin password never reaches the application.

```
WebMVC portal  ->  WebAPI  ->  IKeycloakAdminService  ->  Keycloak Admin REST API
  checklist UI      MediatR       service account token      /admin/realms/stadiapass/...
```

- A **business role** is a Keycloak composite realm role; the permissions ticked in the checklist become its
  composites, so a member token expands to carry every one of those permission strings.
- The checklist is rendered from `StadiaPassPermissions.Groups`, so adding a constant to the shared kernel
  makes it appear in the portal with no UI change. Missing permission roles are created in Keycloak on demand.
- Permission roles cannot be deleted or reused as a business role name - the portal guards both.
- Keycloak built-ins (`offline_access`, `uma_authorization`, `default-roles-*`) are filtered out.

| Method | Route | Required permission |
|---|---|---|
| `GET` | `/api/v1/roles` | `StadiaPass.Roles.Manage` |
| `POST` | `/api/v1/roles` | `StadiaPass.Roles.Manage` |
| `PUT` | `/api/v1/roles/{name}/permissions` | `StadiaPass.Roles.Manage` |
| `DELETE` | `/api/v1/roles/{name}` | `StadiaPass.Roles.Manage` |
| `GET` | `/api/v1/users` | `StadiaPass.Users.Manage` |
| `POST` | `/api/v1/users` | `StadiaPass.Users.Manage` |
| `PUT` | `/api/v1/users/{id}` | `StadiaPass.Users.Manage` |
| `PUT` | `/api/v1/users/{id}/roles` | `StadiaPass.Users.Manage` |
| `DELETE` | `/api/v1/users/{id}` | `StadiaPass.Users.Manage` |

Portal screens live under `/Admin/Roles` and `/Admin/Users` and only appear in the navigation when the
signed-in user carries `Roles.Manage` or `Users.Manage`.

### Session storage

An administrator carries many roles, which makes the OIDC tokens - and therefore the authentication ticket -
large. The MVC app keeps the ticket in Redis behind an `ITicketStore` and leaves only a session key in the
cookie, so sign-in works regardless of how many roles a user has and sign-out revokes the session for real.

## Logging

Serilog owns the logging pipeline of both applications. It is wired once, inside `AddServiceDefaults`, so a
new service gets the same configuration by referencing ServiceDefaults - there is no `UseSerilog()` call to
remember in a `Program.cs` and no second registration to drift.

```
ILogger<T>  (Microsoft.Extensions.Logging API - the call sites never mention Serilog)
  └── Serilog
        ├── Console          human-readable, invariant culture
        └── OpenTelemetry    OTLP -> Aspire dashboard, structured attributes intact
```

Every event carries `ApplicationName`, `Environment` and `ThreadId`, and while an HTTP request is in flight
also `CorrelationId` plus the `UserId` and `UserName` resolved from the Keycloak token. The correlation id is
taken from an `X-Correlation-ID` header when the caller supplies one and otherwise from the current
`Activity`, so a log line and a distributed trace point at the same id.

The MediatR `LoggingBehavior` pushes the command or query itself onto Serilog's `LogContext`, destructured.
Every event raised anywhere inside the handler - including the ones the handler writes itself - therefore
carries the parameters that produced it and the caller who asked for it:

```json
{
  "message": "Handled CreateUserCommand in 185.2638 ms",
  "RequestName": "CreateUserCommand",
  "UserId": "1808c6d6-c5f3-414b-b3bd-62ce83cf7373",
  "UserName": "mudur",
  "CorrelationId": "9d02279fd3dfdc7bd0868d2d2b759e05",
  "Request": {
    "Username": "serilogprobe",
    "Email": "serilogprobe@example.com",
    "Password": "***redacted***",
    "Roles": ["Customer"]
  }
}
```

`CreateUserCommand` carries a password, which is what makes blanket destructuring a liability. A
destructuring policy in ServiceDefaults masks every member whose name contains `password`, `secret`, `token`,
`credential`, `apikey` or `accesscode` before the event is written, so a credential cannot reach a sink even
when a future command adds one.

Noise is kept out on purpose - a log nobody can read is a log nobody reads:

| Source | Level | Why |
|---|---|---|
| `Microsoft.*`, `System.*` | Warning | the framework narrates every request three times |
| `Microsoft.EntityFrameworkCore.Database.Command` | Warning | otherwise every SQL statement, at Information |
| `Polly` | Warning | one line per attempt for every outbound call to Keycloak |
| `/health`, `/alive`, `/metrics` | Verbose | Prometheus scrapes every 5 s; these would dominate the log |

`UseSerilogRequestLogging` replaces the framework's three lines per request with one carrying the method,
route, status code and duration. In the API it is the outermost middleware, so the line reports the status
the caller actually received; in the MVC app it sits after the static file handler, so a page view stays one
line instead of a dozen.

Both `Program.cs` files create a bootstrap logger before the host exists, so a failure during startup - a bad
connection string, an unreachable Keycloak - is written out instead of disappearing with the process.

## Metrics and dashboards

`StadiaPass.ServiceDefaults` already collected OpenTelemetry metrics and pushed them to the Aspire dashboard
over OTLP. The same meters are now also published on a Prometheus scrape endpoint, so a pull-based stack can
read them without a collector in between.

```
ServiceDefaults meters
  ├── OTLP push  ──►  Aspire dashboard        (live, per-run)
  └── /metrics   ◄──  Prometheus (scrape 5s)  ──►  Grafana
```

| Resource | URL | Notes |
|---|---|---|
| Prometheus | http://localhost:9090 | scrapes the API and the MVC app every 5 s |
| Grafana | http://localhost:3000 | anonymous admin in development, `admin` / `admin` otherwise |
| Scrape endpoint | http://localhost:5042/metrics | published by ServiceDefaults, Development only |

Both containers are provisioned from files under `orchestrator/StadiaPass.AppHost/monitoring`, so a fresh
clone comes up with the data source connected and the dashboard already in the **StadiaPass** folder:

```
monitoring
├── prometheus/prometheus.yml                     # scrape jobs for webapi and webmvc
└── grafana
    ├── provisioning/datasources/prometheus.yml   # data source, auto-registered
    ├── provisioning/dashboards/dashboards.yml    # file provider
    └── dashboards/stadiapass-runtime.json        # the dashboard itself
```

The applications run on the host while Prometheus runs in a container, so the scrape targets are
`host.docker.internal:5042` and `:5230`. Grafana reaches Prometheus by its Aspire resource name on the shared
container network, which keeps the data source independent of whichever host port Aspire publishes.

**StadiaPass runtime** dashboard, written against the metric names the exporter actually emits:

| Panel | Query |
|---|---|
| Requests / sec, p95 duration, 5xx rate | `http_server_request_duration_seconds_*` |
| Duration by route | `histogram_quantile` over `http_route` |
| Working set and GC heap | `dotnet_process_memory_working_set_bytes`, `dotnet_gc_last_collection_heap_size_bytes` |
| Allocation rate and GC pauses | `dotnet_gc_heap_total_allocated_bytes_total`, `dotnet_gc_pause_time_seconds_total` |
| CPU and thread pool | `dotnet_process_cpu_time_seconds_total`, `dotnet_thread_pool_thread_count_total` |
| Exceptions and lock contention | `dotnet_exceptions_total`, `dotnet_monitor_lock_contentions_total` |
| PostgreSQL command duration | `db_client_operation_duration_seconds_bucket` |

## Secrets

No secret is written down anywhere in this repository. The database password, the Keycloak client secrets and
the Stripe key all live in **HashiCorp Vault**, and the only thing an application is given is an address and
a token.

```
AppHost                         Vault (KV v2)                 WebAPI / WebMVC
  resolves what only it knows  ──►  secret/stadiapass  ──►  IConfiguration
  (generated ports, passwords)                              (added last, so it wins)
```

`AddVaultConfiguration()` is the first line of both `Program.cs` files, before anything reads configuration -
a connection string is resolved while the container is being built, so a source added later would arrive
after the thing that needed it. It registers an ordinary `ConfigurationProvider`, which is what keeps the
rest of the codebase unaware that Vault exists: `IOptions<T>`, `GetConnectionString` and everything else
carry on exactly as before.

| Key in Vault | Used by |
|---|---|
| `ConnectionStrings:stadiapassdb` | EF Core |
| `ConnectionStrings:cache` | Redis cache and the MVC ticket store |
| `ConnectionStrings:messaging` | MassTransit / RabbitMQ |
| `Keycloak:AdminClientSecret` | `KeycloakAdminService` service account |
| `Keycloak:ClientSecret` | MVC OpenID Connect login |
| `PaymentProvider:Type`, `PaymentProvider:SecretKey` | the payment provider strategy |

### No fallbacks

The options that carry a secret have **no default value** and are `[Required]` with `ValidateOnStart`. A
secret with a working default is a secret that keeps quietly working after someone forgets to configure it,
and then travels into production. Start the API without Vault and it stops immediately:

```
OptionsValidationException: DataAnnotation validation failed for 'KeycloakAdminOptions' members:
'AdminClientSecret' with the error: 'Keycloak:AdminClientSecret is not set. It is expected to come from Vault.'
```

### Development

There is no `Aspire.Hosting.Vault` package, so the AppHost orchestrates the official image directly: a
dev-mode server, in memory and unsealed, with the root token `stadiapass-root-token`. Once it reports ready
the AppHost writes the values it alone can resolve - the ports and passwords Aspire generated for Postgres,
Redis and RabbitMQ - and passes the Stripe key through from its own environment:

```powershell
$env:PaymentProvider__Type = "Stripe"
$env:PaymentProvider__SecretKey = "sk_test_..."
dotnet run --project orchestrator\StadiaPass.AppHost
```

The UI is on the Aspire dashboard as **Vault UI** (http://localhost:8200), token `stadiapass-root-token`.
Nothing survives a restart, which is the point: a dev secret store that persists is a dev secret store that
eventually holds something real.

### Moving to a deployment

Three things change, none of them in application code:

1. The dev container becomes a real Vault cluster, and `Vault__Address` points at it.
2. `Vault__Token` stops being a root token. Vault issues a scoped one through AppRole, Kubernetes auth or the
   agent sidecar; the provider takes a token however it is obtained.
3. Nothing seeds Vault from the orchestrator any more - Vault is the source of truth, and the AppHost's
   seeding step exists only so a clone comes up working.

## Concurrency

Two people want the same seat. Everything below exists for that one sentence.

### The seat: PostgreSQL `xmin`

Both requests read the seat, both find it theirs to buy, both write. Without a guard the second write simply
lands on top of the first: one seat, two tickets, two people at the same turnstile.

PostgreSQL already stamps every row with the id of the transaction that last wrote it, in a hidden system
column called `xmin`. Mapping that as the concurrency token turns every UPDATE into a conditional one:

```sql
UPDATE stadiapass.match_seats SET "HolderReference" = @p0, "Status" = @p1
WHERE "Id" = @p2 AND xmin = @p3
```

Whoever writes second matches zero rows, and EF Core raises `DbUpdateConcurrencyException`. There is no extra
column, no migration and no lock held for the length of a request — which matters here, because this project
creates its schema with `EnsureCreated` and a real version column would never reach a database that already
exists.

`DbUpdateConcurrencyException` is translated into `ConcurrencyConflictException` in `UnitOfWork`, not in the
handler: the application layer does not reference EF Core, and this is the seam that keeps it that way.

### The counters: a relative update

The token protects a seat, not the match. Two people buying two *different* seats of the same match never
touch the same seat row — but both would write back the seat counts they happened to read, and one of the two
sales would quietly vanish from the totals.

The aggregate still moves its own counters in memory, because that is what keeps the domain rules and the
tests that pin them down honest. Those values simply never reach the database. They are marked unmodified and
the totals are worked out by PostgreSQL instead:

```sql
UPDATE stadiapass.matches AS m
SET "ReservedSeatCount" = m."ReservedSeatCount" - 1,
    "SoldSeatCount"     = m."SoldSeatCount" + 1,
    "Status" = CASE WHEN m."AvailableSeatCount" = 0 AND m."ReservedSeatCount" = 1
                    THEN 'SoldOut' ELSE m."Status" END
WHERE "Id" = @match_Id
```

The sold-out test asks whether the reservation being sold is the *last* one, rather than whether the count is
already zero, because every `SET` expression reads the row as it was before the statement. This runs first
inside the transaction, before the seat is touched: it takes the coarsest row, so concurrent sales of one
match queue up here rather than reaching for two rows in opposite orders, which is how deadlocks are made.

### The door: a Redis lock

The token makes a double sale impossible, but it only says so at the very end — by which point the losing
request has had a card charged and refunded for a seat it was never going to get. A charge and a refund on
somebody's statement, for nothing.

So the request is turned away at the door instead, before the seat map is read and before Stripe is called:

```
SET lock:seat:{matchId}:{seatNumber} <token> NX PX 60000
```

Three decisions in that one line are worth more than the line itself.

**Releasing is a compare-and-delete, not a delete.** Once a lease has expired and somebody else holds the
key, a plain `DEL` throws away *their* lock and lets a third caller in beside them. The value stored is a
one-off token, and the release asks about it in a single script, because Redis runs a script without
interleaving anything else:

```lua
if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end
```

**The lease is a minute, not the ten of the reservation window.** A hold is a promise to a customer; this
lease only has to outlive one attempt to pay. Set it to the window and a process that dies mid-purchase makes
the seat unbuyable for ten minutes — including to the very person still holding the reservation, whose hold
would expire while they waited.

**Redis being unreachable is not a reason to stop selling tickets.** The lock fails open with a warning,
because correctness lives in the seat's concurrency token and not here. A cache outage must not become a
sales outage. `null` therefore always means *somebody else holds it*, never *the lock could not be reached*.

> This lock adds no safety the database did not already provide. What it adds is the work that does not
> happen: no seat map read, no charge, no refund, and no confusing pair of lines on a statement.

## Payments

A seat is only sold once the card behind it has been charged. The application layer knows one port,
`IPaymentService`; which adapter answers is a configuration line, so the whole checkout - the decline path
included - runs on a laptop with no Stripe account, no key and no network.

```
ConfirmTicketPurchaseCommandHandler
  ├── 1. IDistributedLock.TryAcquireAsync("lock:seat:{match}:{seat}")   409 if somebody is mid-purchase
  ├── 2. match.EnsureSeatCanBeSoldTo(...)   every rule of the sale, nothing changed yet
  ├── 3. IPaymentService.ProcessPaymentAsync(...)   ──► Mock  (local)
  │                                                 └─► Stripe (test API)
  ├── 4. match.ConfirmSeatSale(...) + Ticket.IssueFor(...)
  └── 5. one transaction ─┬── atomic counter update on the match row
                          ├── seat + ticket, guarded by the seat's xmin
                          └── the TicketPurchasedEvent, into the outbox
                              └── on failure: RefundPaymentAsync, then 409
```

The order is the whole design. The rules run **before** the card is touched, because charging someone and
only then discovering their hold had expired leaves them paid up and seatless. Nothing is written until the
charge succeeds, so a decline needs no compensation: the seat is still `Reserved` for that customer until
the hold runs out, and they can try another card. A decline comes back as **422** with the provider's code:

```json
{ "title": "Payment declined", "status": 422,
  "detail": "The card has insufficient funds.", "paymentFailureCode": "insufficient_funds" }
```

### When the money moved and the sale did not

Step 3 and step 5 cannot be made one atomic act: a card is charged at a company on the other side of the
internet, and a seat is sold in a database here. Between them the sale can still fail — the seat can go to
somebody who got to the row first, or the connection can drop. The customer would be paid up and seatless,
which is the one outcome worth writing code to avoid.

So every exit from step 5 that is not success gives the money back before the exception travels any further,
and only then answers **409**. Two details make that safe rather than merely well-intentioned:

- **The refund is not given the request's cancellation token.** By then the customer may well have closed the
  tab, and that is no reason to keep their money. It runs on `CancellationToken.None`.
- **The refund carries an idempotency key** of `refund:{transactionId}`, so an attempt that is retried gives
  the money back once. Stripe answers a repeat with the same refund id rather than making a second one.

The charge is keyed the same way — `stadiapass:{matchId}:{seatNumber}` — which is what makes a double-click
one charge instead of two.

A refund that itself fails is logged at `Error` with the provider's transaction id rather than thrown: the
caller is already on its way to an error, and replacing that error with this one would hide what actually
went wrong. That log line is what a person needs to give the money back by hand.

> **This is compensation, not atomicity.** Nothing can make a charge and a database write commit together.
> What is claimed here is narrower and testable: money never stays taken for a seat that was never sold, and
> no retry of that unwind takes or returns it twice.

### Choosing a provider

Both values come from **Vault**, under `PaymentProvider:Type` and `PaymentProvider:SecretKey` — see
[Secrets](#secrets) for how they get there and how to change them. Nothing in the application knows where
they came from: the provider strategy reads ordinary configuration.

Only a test key is accepted. A `sk_live_` key would charge real cards from a development machine, so it is
refused at **startup** rather than at the first checkout.

| `Type` | Adapter | Behaviour |
|---|---|---|
| `Mock` (default) | `MockPaymentService` | never leaves the process; `4242…` is accepted, `4000…` comes back as insufficient funds, anything else is declined |
| `Stripe` | `StripePaymentService` | creates and confirms a PaymentIntent against Stripe's test API, keyed by the seat so a double-click is not a second charge |

A `Stripe` provider without a key, or with a key that is not `sk_test_`, fails at **startup** rather than at
the first checkout - a live key on a development machine would charge real cards.

### What happens to the card

Nothing keeps it. The details go from the form to the provider and out of scope with the request: no column,
no cache, no TempData across the redirect. The command's `CardNumber` and `Cvv` are masked by the log
destructuring policy before any event is written, and `PaymentCard` renders as `**** **** **** 4242`
wherever something writes one by accident:

```json
"Request": { "SeatNumber": "GUNEY-1-3", "CardHolderName": "FURKAN PASAOGLU",
             "CardNumber": "***redacted***", "Cvv": "***redacted***" }
```

The number is checked against the Luhn digit before a provider is called at all, so a typo costs a round trip
to the browser instead of a decline on the customer's statement.

### Why the Stripe adapter never sends the card

Stripe refuses a raw card number from a server - *"Sending credit card numbers directly to the Stripe API is
generally unsafe"* - unless the account is specifically approved for it. That refusal is the right one, so
the adapter follows it: `StripeTestCards` maps each of Stripe's published test numbers to the payment
method token that stands for it, and only the token is sent. A card that is not one of them is answered
locally, with an explanation, rather than pushed at Stripe to be rejected.

| Card | Result |
|---|---|
| `4242 4242 4242 4242` | accepted |
| `4000 0000 0000 9995` | `insufficient_funds` |
| `4000 0000 0000 0002` | `generic_decline` |
| `4000 0000 0000 0069` | `expired_card` |
| `4000 0000 0000 0127` | `incorrect_cvc` |

> **Still not a production integration.** The card reaches this server before being turned into a token,
> which puts a real deployment inside PCI DSS scope. In production the browser tokenises it with Stripe.js
> or Elements and the server only ever handles a payment method id - the same shape the adapter already
> works in, so the port does not change: only where the token comes from does.

## Messaging

Selling a ticket is where the customer's request ends and several other things begin: render the ticket, send
the confirmation, update whatever wants to know. None of that should be able to slow down a checkout, and
none of it should be able to fail one.

The obvious arrangement — commit the sale, then publish an event — has a gap in the middle of it. The process
can stop between the two, and then a ticket is sold that nobody downstream is ever told about: no ticket, no
mail, and nothing in the system that knows it is missing. Publishing first is worse: it announces a sale that
a rollback can still take away.

### The outbox

So the message is not published at all. It is **written into the same transaction as the sale**, and the two
share one fate:

```
ConfirmTicketPurchaseCommandHandler
  └── UnitOfWork.ExecuteInTransactionAsync
        ├── counters       (relative update)
        ├── seat + ticket  (guarded by xmin)
        └── outbox_messages row   ◄── the TicketPurchasedEvent, as JSON
                    │
                    │  OutboxProcessor, every 5s
                    ▼
              RabbitMQ ──► ticket-purchased-event ──► TicketPurchasedEventConsumer
```

| Column | |
|---|---|
| `id` | UUIDv7, so the rows are written in roughly the order they happened |
| `occurred_on_utc` | ordering, and the partial index the sweeper reads |
| `type` | the full type name, which is what says how to read the content back |
| `content` | the message as JSON |
| `processed_on_utc` | null until the broker has taken it — this column *is* the queue |
| `error` | why the last attempt did not work, so a stuck message can be explained |

A rollback takes the message with the sale. A broker that is down is a **delay**, not a lost ticket: the row
waits, the reason is written next to it, and the next sweep tries again.

### The sweeper

```sql
SELECT * FROM stadiapass.outbox_messages
WHERE processed_on_utc IS NULL
ORDER BY occurred_on_utc
LIMIT 20
FOR UPDATE SKIP LOCKED
```

`FOR UPDATE SKIP LOCKED` is what lets a second instance of the API run this worker too. It takes the *next*
batch rather than waiting for this one, and never the same rows — without it, two instances would publish
every message twice. The batch is small on purpose: the rows are locked for as long as it takes, and a large
batch holds rows another worker could have been getting on with.

The whole sweep runs inside `Database.CreateExecutionStrategy()`. The Aspire Npgsql component configures a
*retrying* strategy, and a retrying strategy refuses to have a transaction opened behind its back — it would
have no way to replay it. `UnitOfWork` has the same shape for the same reason.

A type name coming out of a database row is resolved against `IntegrationEventTypes`, a fixed list, rather
than by asking the runtime for whatever the row happens to say. Rows only get there through code in this
solution today, and that is exactly the assumption worth not building on. An unregistered message is refused
when it is written, not discovered five seconds later.

### The bus

MassTransit over RabbitMQ, with the publisher and the consumer in the same process for now — which is an
ordinary way to run a system that has not been split up yet, and also the point: the message really does
leave for the broker and really does come back, so the day a consumer moves into its own service, this side
does not change.

| | |
|---|---|
| Exchange | `StadiaPass.Application.Tickets.Events:TicketPurchasedEvent` |
| Queue | `ticket-purchased-event` (kebab-case formatter, so the management page reads the way people expect) |
| Package | MassTransit **8.5.10** — the last Apache-2.0 line, pinned for the same reason MediatR is pinned at 12 |

The persistence layer never references MassTransit. It publishes through an `IEventBus` port, and the
MassTransit adapter for it lives in the infrastructure layer alongside the Stripe and Redis ones.

> **Delivery is at least once, never exactly once.** A message can reach the broker and the row that records
> it can still fail to commit, and the only honest answer is to send it again. Consumers must be able to see
> the same purchase twice without doing the work twice — check for the ticket before rendering it, and for
> the mail before sending it. Two identical confirmation mails is a small embarrassment; two charges would
> not be.
>
> There is also no attempt counter yet, so a message that can never be delivered is retried forever. One
> column and a ceiling is what closes that.

## Request pipeline

```
HTTP → Minimal API endpoint → ISender.Send(command)
     → LoggingBehavior → ValidationBehavior (FluentValidation)
     → Handler → IDistributedLock → Aggregate behaviour → IPaymentService → Repository
     → UnitOfWork.ExecuteInTransactionAsync (counters + seat + ticket + outbox row)
     → publish domain events in process (MediatR notifications)
     → OutboxProcessor → RabbitMQ → consumers (out of band)
```

Domain events stay in process: they are about this transaction and its own invariants, and MediatR is the
right size for that. `TicketPurchasedEvent` is not one of them — it is an integration message, carrying
everything a consumer needs so it can reach one that has no database of ours to ask.

The seat holder is taken from `ICurrentUser` (the Keycloak subject), never from the request body, so a
customer cannot hold or buy a seat in somebody else's name.

## Tests

```powershell
dotnet test StadiaPass.slnx
```

| Project | Covers |
|---|---|
| `tests/StadiaPass.Domain.UnitTests` | aggregate invariants: match creation, the seat lifecycle, venue seating plans |
| `tests/StadiaPass.Application.UnitTests` | the `ReserveSeat` and `ConfirmTicketPurchase` slices with their ports substituted: the order of payment and persistence, the refund on a lost seat, the lock at the door, and the announcement written inside the sale transaction |

xUnit, NSubstitute and FluentAssertions, named `Should_X_When_Y` and written Arrange-Act-Assert.

The domain tests build **real** aggregates - a test that stubbed the domain would prove nothing about the
rule it is meant to guard. Time is injected everywhere, so a test reasoning about the ten-minute hold never
races the wall clock. The handler tests substitute `IMatchRepository`, `IUnitOfWork`, `ICurrentUser` and
`IDateTimeProvider` and still drive the genuine `Match` aggregate underneath.

What the suite pins down:

- a match cannot be opened in a venue whose kind the category does not allow, nor for an inactive category
- creating a match materialises every seat of the venue plan and applies each block's price multiplier
- reserving an available seat holds it for exactly `Match.ReservationWindow`, moves it out of the available
  pool and raises `SeatReservedDomainEvent`
- a seat that is already `Reserved` or `Sold` is refused, and a refused attempt leaves the counters untouched
- an expired hold is handed to the next buyer; only the holder can turn a hold into a sale, and only in time
- the handler reserves for the caller from `ICurrentUser`, saves exactly once, and does not save at all when
  the domain refuses

Those guarantees were checked by mutation rather than taken on trust: deleting the double-booking guard turns
4 domain tests red, and reading the wrong field off `ICurrentUser` turns 7 handler tests red.

`tests/Directory.Build.props` re-imports the repository settings and relaxes only CA1707 and CA2007, which do
not apply to test code. `StadiaPass.Application` grants `InternalsVisibleTo` to its test project because
handlers are internal by design.

## Notes

- **Migrations**: the starter uses `EnsureCreatedAsync` plus seeding for a one-command run. Switch to
  `dotnet ef migrations add Initial -p src/Infrastructure/StadiaPass.Persistence -s src/Presentation/StadiaPass.WebAPI`
  and `Database.MigrateAsync()` before any real deployment. Changing the model currently means dropping the
  `stadiapass-pgdata` volume — `EnsureCreated` builds the schema once and never looks at it again, so a table
  added afterwards never appears on a database that already exists. `outbox_messages` is the first table that
  ran into this, and it asks for itself with a `CREATE TABLE IF NOT EXISTS` in the initializer. That is a
  stopgap and reads like one; migrations are what removes it.
- **Seat map loading**: `GetWithSeatAsync` uses a filtered `Include`, so reserving a seat in a 20 000-seat
  venue touches a single row. Only the seat map screen loads the full collection.
- **Value objects and EF Core**: an owned instance may never be shared between two owners. Each seat gets its
  own `Money`, and a ticket snapshots a copy of the seat price rather than reusing the instance.
- **Timestamps**: Npgsql only accepts `DateTimeOffset` values with a zero offset for `timestamptz`, so
  `Match` normalises the kick-off to UTC on the way in.
- **MediatR** is pinned to `12.5.0`, the last Apache-2.0 release; v13+ requires a commercial licence.
- **MassTransit** is pinned to `8.5.10` for the same reason: v9 moved to a commercial licence.
- **FluentAssertions** is pinned to `7.2.0` for the same reason: from v8 it moved to a paid licence for
  commercial use.
- **Aspire Keycloak integration** (`Aspire.Hosting.Keycloak`, `Aspire.Keycloak.Authentication`) is still
  prerelease; the pinned version matches the Aspire 13.5.2 SDK.
- **`stadiapass-admin-api`** is a confidential client whose service account holds the `realm-management`
  roles the portal needs. Its secret in `stadiapass-realm.json` is a local development value.
- **Culture is pinned to the invariant culture** in the MVC app. Model binding and Razor rendering must
  agree with jQuery unobtrusive validation, which parses numbers with a dot. Under a Turkish culture the
  server rendered `500,00` while the client validator read it as `NaN`, and a typed `500.50` bound as
  `50050`. Decimal inputs are also rendered as `type="number"` so `step` and `min` actually apply.
- **Everything runs on `localhost`**, and cookies are not scoped by port, so the MVC app, the API, Keycloak
  and the Aspire dashboard share one cookie jar. Abandoned sign-ins used to leave correlation and nonce
  cookies behind until the request header grew past what Keycloak accepts and it answered `431`. Those
  cookies now expire after five minutes, and Keycloak runs with a raised header limit. If you ever hit a
  `431` again, clearing the `localhost` cookies is the fix.
- **`stadiapass-mvc` has direct access grants enabled** so tokens can be fetched with `curl` for local
  endpoint testing. Turn it off before deploying.
