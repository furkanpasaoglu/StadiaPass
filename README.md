# StadiaPass

Stadium ticketing system built as a reference-grade **.NET 10 / C# 14** Clean Architecture solution:
Minimal API backend, MVC front end, DDD domain model, CQRS with MediatR, and .NET Aspire orchestration.

## Architecture

```
StadiaPass.slnx
├── Directory.Build.props            # shared MSBuild settings (net10.0, nullable, warnings-as-errors)
├── Directory.Packages.props         # Central Package Management - single source of truth for versions
├── src
│   ├── Core
│   │   ├── StadiaPass.Domain        # entities, aggregates, value objects, domain events (no infra deps)
│   │   │   ├── Abstractions         # IRepository, ITicketRepository, IMatchRepository, IUnitOfWork
│   │   │   ├── Common               # Entity, AggregateRoot, DomainEvent, DomainException
│   │   │   │   └── ValueObjects     # Money, SeatNumber
│   │   │   ├── Matches              # Match aggregate + MatchStatus + events
│   │   │   └── Tickets              # Ticket aggregate + TicketStatus + events
│   │   └── StadiaPass.Application   # CQRS use cases (MediatR), validation, DTOs
│   │       ├── Common
│   │       │   ├── Abstractions     # IDateTimeProvider, ICacheService
│   │       │   ├── Authorization    # StadiaPassPermissions - the only place a permission is declared
│   │       │   ├── Behaviors        # LoggingBehavior, ValidationBehavior (MediatR pipeline)
│   │       │   └── Exceptions       # NotFoundException, ConflictException, RequestValidationException
│   │       ├── Matches              # Commands / Queries / MatchDto
│   │       └── Tickets              # Commands / Queries / EventHandlers / TicketDto
│   ├── Infrastructure
│   │   ├── StadiaPass.Persistence   # EF Core 10 + PostgreSQL, repositories, Unit of Work
│   │   │   ├── Configurations       # IEntityTypeConfiguration per aggregate
│   │   │   └── Repositories         # Repository<T>, TicketRepository, MatchRepository
│   │   └── StadiaPass.Infrastructure# cross-cutting adapters: Redis cache, system clock
│   └── Presentation
│       ├── StadiaPass.WebAPI        # Minimal API - MapGroup + IEndpoint discovery + Scalar reference
│       │   ├── Authorization        # dynamic permission policies + Keycloak claims transformation
│       │   ├── Contracts            # transport-level request records
│       │   ├── Endpoints            # TicketEndpoints, MatchEndpoints, IEndpoint, EndpointExtensions
│       │   └── Extensions           # GlobalExceptionHandler, OAuth2 OpenAPI transformers
│       └── StadiaPass.WebMVC        # Razor MVC UI - consumes the API over HTTP only
│           ├── Authentication       # OIDC login, KeycloakOptions, TokenBearerHandler
│           ├── Controllers          # TicketsController, AccountController
│           ├── Models               # its own contracts - no reference to Domain/Application
│           └── Services             # typed HttpClient (IStadiaPassApiClient)
├── orchestrator
│   ├── StadiaPass.AppHost           # Aspire: PostgreSQL + Redis + Keycloak, project wiring
│   │   └── realms                   # stadiapass-realm.json - roles, clients, demo users
│   └── StadiaPass.ServiceDefaults   # OpenTelemetry, health checks, resilience, service discovery
└── tests                            # (reserved for Domain/Application unit tests)
```

### Dependency rule

```
WebMVC ──HTTP──► WebAPI ──► Application ──► Domain
                    │            ▲
                    └──► Persistence / Infrastructure (implement Domain + Application abstractions)
```

`Domain` depends on nothing but `MediatR.Contracts` (marker interfaces only).
`WebMVC` never references `Domain` or `Application` — it is a pure API consumer, exactly like a
third-party client would be.

## Domain model

| Aggregate | Invariants enforced in the aggregate |
|---|---|
| `Match` | teams differ, kick-off in the future, capacity > 0, tickets only issued while `Scheduled`/`OnSale`, auto `SoldOut` at capacity |
| `Ticket` | only `Available` → `Reserve()`, only `Reserved` → `ConfirmSale()`, 15-minute reservation window, price > 0 |

State is mutated only through behaviour (`Schedule`, `OpenSales`, `Issue`, `Reserve`, `ConfirmSale`,
`ReleaseReservation`); every setter is `private`. Rule violations throw `DomainRuleViolationException`,
which the API maps to `422 Unprocessable Content`.

## Running

Requires the .NET 10 SDK and a container runtime (Docker Desktop or Podman).

```powershell
dotnet run --project orchestrator/StadiaPass.AppHost
```

Aspire starts PostgreSQL (with pgAdmin), Redis (with RedisInsight), Keycloak, the API and the MVC app, then
opens the dashboard. The database schema is created and seeded with two demo matches, and the Keycloak realm
is imported, on start.

| Resource | Default local URL |
|---|---|
| MVC UI | http://localhost:5230 |
| Keycloak | https://localhost:8080 |
| API | http://localhost:5042 |
| API reference (Scalar) | http://localhost:5042/scalar/v1 |
| OpenAPI document | http://localhost:5042/openapi/v1.json |
| Health | http://localhost:5042/health |

The Aspire dashboard exposes the Scalar reference directly as a link on the `webapi` resource.
Both the OpenAPI document and Scalar are mapped only in the Development environment.

## API surface

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/matches` | upcoming matches (Redis-cached, 30 s) |
| `POST` | `/api/v1/matches` | schedule a match and open sales |
| `GET` | `/api/v1/tickets?matchId={id}` | tickets issued for a match |
| `POST` | `/api/v1/tickets` | issue a ticket for a seat |
| `GET` | `/api/v1/tickets/{ticketId}` | single ticket |
| `POST` | `/api/v1/tickets/{ticketId}/reservation` | reserve an available ticket |
| `POST` | `/api/v1/tickets/{ticketId}/sale` | confirm the sale of a reserved ticket |

Errors are returned as `ProblemDetails`: `400` validation, `404` not found, `409` seat conflict,
`422` domain rule violation.

## Authorization

Authentication is delegated to Keycloak; authorization is **permission-based and fully dynamic**. No role
name appears anywhere in the code.

```
Keycloak realm role  ──►  KeycloakPermissionClaimsTransformation  ──►  "permission" claim
   "StadiaPass.Tickets.Create"        (filtered against StadiaPassPermissions)
                                              │
        .RequireAuthorization(StadiaPassPermissions.Tickets.Create)
                                              │
                              PermissionPolicyProvider  ──► builds the policy on demand
                                              │
                              PermissionAuthorizationHandler  ──► 200 / 403
```

- `StadiaPassPermissions` (Application layer) is the only place a permission string is declared; `All` is
  discovered by reflection over its nested constants and backed by a `FrozenSet`.
- `PermissionPolicyProvider` creates an `AuthorizationPolicy` for any known permission the first time it is
  requested, so `AddPolicy(...)` never has to be written by hand.
- Roles that the application does not recognise are dropped by the claims transformation instead of being
  trusted, so adding a role in Keycloak cannot silently widen access.
- Adding a permission is a two-step change: add the constant, add the matching realm role. No policy
  registration, no `[Authorize(Roles = ...)]`, no redeploy of the authorization plumbing.

| Endpoint | Required permission |
|---|---|
| `GET /api/v1/matches` | `StadiaPass.Matches.View` |
| `POST /api/v1/matches` | `StadiaPass.Matches.Create` |
| `GET /api/v1/tickets` | `StadiaPass.Tickets.View` |
| `POST /api/v1/tickets` | `StadiaPass.Tickets.Create` |
| `POST /api/v1/tickets/{id}/reservation` | `StadiaPass.Tickets.Reserve` |
| `POST /api/v1/tickets/{id}/sale` | `StadiaPass.Tickets.Sell` |

### Testing from Scalar

The OpenAPI document publishes an OAuth2 authorization code flow, so the Scalar reference renders an
**Authorize** button that redirects to Keycloak (PKCE `S256`, public client `stadiapass-scalar`) and injects
the resulting token into every request. Each secured operation is annotated with the permission it needs.

### Demo users (realm import, development only)

| User | Password | Permissions |
|---|---|---|
| `mudur` | `mudur` | everything |
| `gise` | `gise` | all ticket operations + `Matches.View` |
| `seyirci` | `seyirci` | `Tickets.View`, `Matches.View` only |

Keycloak runs at https://localhost:8080 and the realm is re-imported on every start (no data volume), so
the realm file is the source of truth. The MVC client secret in `stadiapass-realm.json` is a local
development value - replace it with a real secret store before any deployment.

## Request pipeline

```
HTTP → Minimal API endpoint → ISender.Send(command)
     → LoggingBehavior → ValidationBehavior (FluentValidation)
     → Handler → Aggregate behaviour → Repository
     → UnitOfWork.SaveChangesAsync → publish domain events (MediatR notifications)
```

## Notes

- **Migrations**: the starter uses `EnsureCreatedAsync` plus seeding for a one-command run. Switch to
  `dotnet ef migrations add Initial -p src/Infrastructure/StadiaPass.Persistence -s src/Presentation/StadiaPass.WebAPI`
  and `Database.MigrateAsync()` before any real deployment.
- **MediatR** is pinned to `12.5.0`, the last Apache-2.0 release; v13+ requires a commercial licence.
- **SQL Server instead of PostgreSQL**: swap `AddPostgres`/`AddDatabase` in `AppHost.cs` for
  `AddSqlServer`, and `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` for
  `Aspire.Microsoft.EntityFrameworkCore.SqlServer` in `Persistence`.
