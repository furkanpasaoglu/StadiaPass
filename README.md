# StadiaPass

Stadium and arena ticketing built as a reference-grade **.NET 10 / C# 14** Clean Architecture solution:
Minimal API backend, MVC front end, DDD domain model, CQRS with MediatR, Keycloak-backed dynamic permission
authorization, and .NET Aspire orchestration.

Matches belong to a sport category and are opened against a venue seating plan. Customers pick a seat off an
interactive map, the seat is held for them, and the purchase turns that hold into a ticket.

## Architecture

```
StadiaPass.slnx
├── Directory.Build.props            # shared MSBuild settings (net10.0, nullable, warnings-as-errors)
├── Directory.Packages.props         # Central Package Management - single source of truth for versions
├── src
│   ├── Shared
│   │   └── StadiaPass.SharedKernel  # permission contracts shared by the API and the MVC front end
│   │       └── Authorization        # StadiaPassPermissions, dynamic policies, Keycloak role reader
│   ├── Core
│   │   ├── StadiaPass.Domain        # aggregates, value objects, domain events (no infrastructure deps)
│   │   │   ├── Abstractions         # IRepository, IVenueRepository, IMatchRepository, ITicketRepository, IUnitOfWork
│   │   │   ├── Common               # Entity, AggregateRoot, DomainEvent, DomainException
│   │   │   │   └── ValueObjects     # Money, SeatNumber
│   │   │   ├── Venues               # Venue aggregate + VenueBlock + VenueKind (the seating plan)
│   │   │   ├── Matches              # Match aggregate + MatchSeat + SportCategory + SeatStatus + events
│   │   │   └── Tickets              # Ticket aggregate + TicketStatus + events
│   │   └── StadiaPass.Application   # CQRS use cases (MediatR), validation, DTOs
│   │       ├── Common
│   │       │   ├── Abstractions     # IDateTimeProvider, ICacheService, ICurrentUser
│   │       │   ├── Behaviors        # LoggingBehavior, ValidationBehavior (MediatR pipeline)
│   │       │   └── Exceptions       # NotFoundException, ConflictException, RequestValidationException
│   │       ├── Venues               # DefineVenue / GetVenues
│   │       ├── Matches              # CreateMatch / GetUpcomingMatches / GetMatchSeatMap / EventHandlers
│   │       └── Tickets              # ReserveSeat / ConfirmTicketPurchase / GetMyTickets / GetTicketById
│   ├── Infrastructure
│   │   ├── StadiaPass.Persistence   # EF Core 10 + PostgreSQL, repositories, Unit of Work, seeding
│   │   │   ├── Configurations       # IEntityTypeConfiguration per aggregate
│   │   │   └── Repositories         # Repository<T>, VenueRepository, MatchRepository, TicketRepository
│   │   └── StadiaPass.Infrastructure# cross-cutting adapters: Redis cache, system clock
│   └── Presentation
│       ├── StadiaPass.WebAPI        # Minimal API - MapGroup + IEndpoint discovery + Scalar reference
│       │   ├── Authorization        # Keycloak JWT wiring, KeycloakOptions, CurrentUser
│       │   ├── Endpoints            # VenueEndpoints, MatchEndpoints, TicketEndpoints, IEndpoint
│       │   └── Extensions           # GlobalExceptionHandler, OAuth2 OpenAPI transformers
│       └── StadiaPass.WebMVC        # Razor MVC UI - consumes the API over HTTP only
│           ├── Areas/Admin          # back-office: match creation form
│           ├── Authentication       # OIDC login, KeycloakOptions, TokenBearerHandler
│           ├── Controllers          # MatchesController (seat picker), TicketsController, AccountController
│           ├── Models               # its own contracts - no reference to Domain/Application
│           └── Services             # typed HttpClient (IStadiaPassApiClient)
├── orchestrator
│   ├── StadiaPass.AppHost           # Aspire: PostgreSQL + Redis + Keycloak, project wiring
│   │   └── realms                   # stadiapass-realm.json - permission roles, clients, demo users
│   └── StadiaPass.ServiceDefaults   # OpenTelemetry, health checks, resilience, service discovery
└── tests                            # (reserved for Domain/Application unit tests)
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
| `Venue` | at least one block, unique block names, plan capped at 25 000 seats, decides which sport categories it can host |
| `Match` | teams differ, kick-off in the future (normalised to UTC), venue must be able to host the category, seats materialised from the venue plan, seat counters and `SoldOut` kept consistent |
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

Aspire starts PostgreSQL (with pgAdmin), Redis (with RedisInsight), Keycloak, the API and the MVC app. On
first start the schema is created and seeded with two venues and three matches (742 seats), and the Keycloak
realm is imported.

| Resource | Default local URL |
|---|---|
| MVC UI | http://localhost:5230 |
| Keycloak | https://localhost:8080 |
| API | http://localhost:5042 |
| API reference (Scalar) | http://localhost:5042/scalar/v1 |
| OpenAPI document | http://localhost:5042/openapi/v1.json |
| Health | http://localhost:5042/health |

The Aspire dashboard exposes the Scalar reference as a link on the `webapi` resource. The OpenAPI document
and Scalar are mapped only in the Development environment.

## API surface

| Method | Route | Required permission |
|---|---|---|
| `GET` | `/api/v1/venues` | `StadiaPass.Venues.View` |
| `POST` | `/api/v1/venues` | `StadiaPass.Venues.Create` |
| `GET` | `/api/v1/matches?category={sport}` | `StadiaPass.Matches.View` |
| `POST` | `/api/v1/matches` | `StadiaPass.Matches.Create` |
| `GET` | `/api/v1/matches/{id}/seats` | `StadiaPass.Matches.View` |
| `POST` | `/api/v1/matches/{id}/seats/{seatNumber}/reservation` | `StadiaPass.Tickets.Reserve` |
| `POST` | `/api/v1/tickets` | `StadiaPass.Tickets.Purchase` |
| `GET` | `/api/v1/tickets/mine` | `StadiaPass.Tickets.View` |
| `GET` | `/api/v1/tickets/{id}` | `StadiaPass.Tickets.View` |

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

### Admin versus customer

| | Admin (`Matches.Create`, `Venues.*`) | Customer (`Tickets.Reserve`, `Tickets.Purchase`) |
|---|---|---|
| MVC | `/Admin/Match/Create` form, "Create match" in the nav | match list, seat picker, my tickets |
| API | define venues, create matches | read seat maps, hold a seat, buy it |

### Testing from Scalar

The OpenAPI document publishes an OAuth2 authorization code flow, so the Scalar reference renders an
**Authorize** button that redirects to Keycloak (PKCE `S256`, public client `stadiapass-scalar`) and injects
the token into every request. Each secured operation is annotated with the permission it needs.

### Demo users (realm import, development only)

| User | Password | Role |
|---|---|---|
| `mudur` | `mudur` | everything (admin) |
| `gise` | `gise` | box office: sell and cancel, no match creation |
| `musteri` | `musteri` | customer: browse, hold a seat, buy |
| `seyirci` | `seyirci` | read-only: cannot hold or buy |

Keycloak runs at https://localhost:8080 and the realm is re-imported on every start (no data volume), so the
realm file is the source of truth. The MVC client secret in `stadiapass-realm.json` is a local development
value — replace it with a real secret store before any deployment.

## Request pipeline

```
HTTP → Minimal API endpoint → ISender.Send(command)
     → LoggingBehavior → ValidationBehavior (FluentValidation)
     → Handler → Aggregate behaviour → Repository
     → UnitOfWork.SaveChangesAsync → publish domain events (MediatR notifications)
```

The seat holder is taken from `ICurrentUser` (the Keycloak subject), never from the request body, so a
customer cannot hold or buy a seat in somebody else's name.

## Notes

- **Migrations**: the starter uses `EnsureCreatedAsync` plus seeding for a one-command run. Switch to
  `dotnet ef migrations add Initial -p src/Infrastructure/StadiaPass.Persistence -s src/Presentation/StadiaPass.WebAPI`
  and `Database.MigrateAsync()` before any real deployment. Changing the model currently means dropping the
  `stadiapass-pgdata` volume.
- **Seat map loading**: `GetWithSeatAsync` uses a filtered `Include`, so reserving a seat in a 20 000-seat
  venue touches a single row. Only the seat map screen loads the full collection.
- **Value objects and EF Core**: an owned instance may never be shared between two owners. Each seat gets its
  own `Money`, and a ticket snapshots a copy of the seat price rather than reusing the instance.
- **Timestamps**: Npgsql only accepts `DateTimeOffset` values with a zero offset for `timestamptz`, so
  `Match` normalises the kick-off to UTC on the way in.
- **MediatR** is pinned to `12.5.0`, the last Apache-2.0 release; v13+ requires a commercial licence.
- **Aspire Keycloak integration** (`Aspire.Hosting.Keycloak`, `Aspire.Keycloak.Authentication`) is still
  prerelease; the pinned version matches the Aspire 13.5.2 SDK.
- **`stadiapass-mvc` has direct access grants enabled** so tokens can be fetched with `curl` for local
  endpoint testing. Turn it off before deploying.
