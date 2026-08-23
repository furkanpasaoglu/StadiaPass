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
│       ├── StadiaPass.WebAPI        # Minimal API - MapGroup + IEndpoint discovery
│       │   ├── Contracts            # transport-level request records
│       │   ├── Endpoints            # TicketEndpoints, MatchEndpoints, IEndpoint, EndpointExtensions
│       │   └── Extensions           # GlobalExceptionHandler (RFC 9457 ProblemDetails)
│       └── StadiaPass.WebMVC        # Razor MVC UI - consumes the API over HTTP only
│           ├── Controllers          # TicketsController
│           ├── Models               # its own contracts - no reference to Domain/Application
│           └── Services             # typed HttpClient (IStadiaPassApiClient)
├── orchestrator
│   ├── StadiaPass.AppHost           # Aspire: PostgreSQL + Redis containers, project wiring
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

Aspire starts PostgreSQL (with pgAdmin), Redis (with RedisInsight), the API and the MVC app, then opens
the dashboard. The database schema is created and seeded with two demo matches on first start.

| Resource | Default local URL |
|---|---|
| MVC UI | http://localhost:5230 |
| API | http://localhost:5042 |
| OpenAPI document | http://localhost:5042/openapi/v1.json |
| Health | http://localhost:5042/health |

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
