# StadiaPass

[English](README.md) · **Türkçe**

Stadyum ve arena biletleme sistemi; referans kalitesinde bir **.NET 10 / C# 14** Clean Architecture çözümü
olarak yazıldı: Minimal API arka uç, MVC ön yüz, DDD domain modeli, MediatR ile CQRS, Keycloak destekli
dinamik izin (permission) yetkilendirmesi ve .NET Aspire orkestrasyonu.

Maçlar bir spor kategorisine ait olur ve bir mekânın koltuk planı üzerinden açılır. Müşteri interaktif
haritadan bir koltuk seçer, koltuk onun adına tutulur, ve satın alma o tutmayı bilete dönüştürür.

Buradaki asıl ilginç kısım o son cümlede saklı. İki kişi aynı koltuğu istiyor, para internetin öbür ucundaki
bir şirkette el değiştiriyor, ve sonrasında olan hiçbir şeyin checkout'u düşürebilmesi gerekmiyor — bu yüzden
koltuk [optimistic concurrency](#eşzamanlılık) ile korunuyor, çekim
[satış tamamlanmazsa telafi ediliyor](#para-gitti-ama-satış-olmadı), ve duyuru
[transactional outbox](#mesajlaşma) üzerinden çıkıyor.

## Mimari

```
StadiaPass.slnx
├── Directory.Build.props            # ortak MSBuild ayarları (net10.0, nullable, warnings-as-errors)
├── Directory.Packages.props         # Central Package Management - sürümler için tek kaynak
├── src
│   ├── Shared
│   │   ├── StadiaPass.SharedKernel  # izin sözlüğü, hiçbir framework bağımlılığı yok
│   │   │   └── Authorization        # StadiaPassPermissions + katalog, KeycloakRoleReader
│   │   └── StadiaPass.SharedKernel.AspNetCore  # dinamik policy provider + claims transformation
│   ├── Core
│   │   ├── StadiaPass.Domain        # aggregate'ler, value object'ler, domain event'leri (altyapı bağımlılığı yok)
│   │   │   ├── Abstractions         # IRepository, IVenueRepository, IMatchRepository, ITicketRepository, IUnitOfWork
│   │   │   ├── Common               # Entity, AggregateRoot, DomainEvent, DomainException
│   │   │   │   └── ValueObjects     # Money, SeatNumber
│   │   │   ├── Categories           # SportCategory aggregate (hangi mekân türlerinde oynanabilir)
│   │   │   ├── Venues               # Venue aggregate + VenueBlock + VenueKind (koltuk planı)
│   │   │   ├── Matches              # Match aggregate + MatchSeat + SportCategory + SeatStatus + event'ler
│   │   │   └── Tickets              # Ticket aggregate + TicketStatus + event'ler
│   │   └── StadiaPass.Application   # CQRS use case'leri (MediatR), doğrulama, DTO'lar
│   │       ├── Common
│   │       │   ├── Abstractions     # IDateTimeProvider, ICacheService, ICurrentUser
│   │       │   ├── Behaviors        # LoggingBehavior, ValidationBehavior (MediatR pipeline)
│   │       │   ├── Exceptions       # NotFound, Conflict, ConcurrencyConflict, PaymentFailed, Validation
│   │       │   └── Messaging        # IntegrationEventTypes - hatta çıkmasına izin verilen mesajlar
│   │       ├── Infrastructure
│   │       │   └── Abstractions     # IPaymentService, IDistributedLock, IOutbox, IEventBus
│   │       ├── Categories           # GetCategories / CreateCategory / UpdateCategory / DeleteCategory
│   │       ├── Identity             # Keycloak Admin portu: Roles / Users dilimleri
│   │       ├── Venues               # GetVenues / CreateVenue / UpdateVenue / DeleteVenue
│   │       ├── Matches              # CreateMatch / GetUpcomingMatches / GetMatchSeatMap / EventHandlers
│   │       └── Tickets              # ReserveSeat / ConfirmTicketPurchase / GetMyTickets / GetTicketById
│   ├── Infrastructure
│   │   ├── StadiaPass.Persistence   # EF Core 10 + PostgreSQL, repository'ler, Unit of Work, seed
│   │   │   ├── Configurations       # aggregate başına IEntityTypeConfiguration
│   │   │   ├── Outbox               # OutboxMessage + writer + mesajı broker'a taşıyan sweeper
│   │   │   └── Repositories         # Repository<T>, VenueRepository, MatchRepository, TicketRepository
│   │   └── StadiaPass.Infrastructure# yukarıdaki portların adaptörleri
│   │       ├── Locking              # Redis SET NX PX + Lua ile compare-and-delete serbest bırakma
│   │       ├── Messaging            # RabbitMQ üzerinde MassTransit + TicketPurchasedEvent consumer'ı
│   │       └── Payments             # Mock ve Stripe adaptörleri, sağlayıcı stratejisi
│   └── Presentation
│       ├── StadiaPass.WebAPI        # Minimal API - MapGroup + IEndpoint keşfi + Scalar referansı
│       │   ├── Authorization        # Keycloak JWT bağlantısı, KeycloakOptions, CurrentUser
│       │   ├── Endpoints            # VenueEndpoints, MatchEndpoints, TicketEndpoints, RoleEndpoints, UserEndpoints
│       │   └── Extensions           # GlobalExceptionHandler, OAuth2 OpenAPI transformer'ları
│       └── StadiaPass.WebMVC        # Razor MVC arayüzü - API'yi yalnızca HTTP üzerinden tüketir
│           ├── Areas/Admin          # back-office: maçlar, mekânlar, kategoriler, roller, kullanıcılar
│           ├── Authentication       # OIDC girişi, KeycloakOptions, TokenBearerHandler
│           ├── Controllers          # MatchesController (koltuk seçici), TicketsController, AccountController
│           ├── Models               # kendi sözleşmeleri - Domain/Application'a referans yok
│           └── Services             # tipli HttpClient'lar (biletleme + kimlik portalı)
├── orchestrator
│   ├── StadiaPass.AppHost           # Aspire: PostgreSQL, Redis, RabbitMQ, Keycloak, Vault, Prometheus, Grafana
│   │   ├── monitoring               # prometheus.yml, Grafana datasource ve dashboard provisioning
│   │   └── realms                   # stadiapass-realm.json - izin rolleri, client'lar, demo kullanıcılar
│   └── StadiaPass.ServiceDefaults   # Vault konfigürasyonu, Serilog, OpenTelemetry, health check'ler
│       └── Logging                  # Serilog kurulumu, request-context enricher, kimlik bilgisi maskeleme
└── tests
    ├── StadiaPass.Domain.UnitTests       # aggregate invariant'ları
    └── StadiaPass.Application.UnitTests  # portları taklit edilmiş dikey dilim handler'ları
```

### Bağımlılık kuralı

```
WebMVC ──HTTP──► WebAPI ──► Application ──► Domain
   │                │            ▲
   │                └──► Persistence / Infrastructure (Domain + Application soyutlamalarını uygular)
   └──────────────► SharedKernel ◄── WebAPI          (yalnızca izin sözleşmeleri)
```

`Domain` yalnızca `MediatR.Contracts`'a bağımlı (sadece marker interface'ler).
`WebMVC` ne `Domain`'e ne `Application`'a referans verir — tıpkı üçüncü taraf bir istemci gibi, saf bir API
tüketicisidir. API ile paylaştığı tek şey izin sözlüğüdür; o da `SharedKernel`'de durur, böylece iki taraftan
hiçbiri kendi kafasına göre bir izin string'i uyduramaz.

## Domain modeli

```
Venue (aggregate)                  Match (aggregate)                 Ticket (aggregate)
  Name, City, Kind                   Category, VenueId                 MatchId, MatchSeatId
  └── VenueBlock[]                   Capacity / koltuk sayaçları       SeatNumber, Price (anlık kopya)
        Name, Rows, SeatsPerRow      └── MatchSeat[]                   HolderReference, AccessCode
        PriceMultiplier                    SeatNumber, Price
                                           Status: Available | Reserved | Sold
```

| Aggregate | Aggregate içinde uygulanan invariant'lar |
|---|---|
| `SportCategory` | en az bir oynanabilir mekân türü, benzersiz ad, pasif kategori yeni maç kabul etmez |
| `Venue` | en az bir blok, benzersiz blok adları, plan 25 000 koltukla sınırlı, bir maç kullandıysa plan dondurulur |
| `Match` | takımlar farklı, başlama saati gelecekte (UTC'ye normalize), kategori mekân türünde oynanabilir olmalı, koltuklar mekân planından üretilir, koltuk sayaçları ve `SoldOut` tutarlı tutulur |
| `MatchSeat` | `Available` → `Reserve()` → `ConfirmSale()`, 10 dakikalık tutma, yalnızca tutan kişi satın alabilir, süresi dolan tutmalar kendiliğinden serbest kalır |
| `Ticket` | yalnızca maçın `Sold` durumuna geçirdiği bir koltuk için kesilebilir |

Koltuk geçişleri **yalnızca** maç üzerinden yürür: `MatchSeat.Reserve/ConfirmSale/Release` `internal`'dır,
dolayısıyla `Match.ReserveSeat(seatNumber, holder, now)` ve `Match.ConfirmSeatSale(...)` tek giriş
noktalarıdır ve sayaçlar koltuklardan asla sapamaz. Her setter `private`; kural ihlalleri
`DomainRuleViolationException` fırlatır ve API bunu `422 Unprocessable Content`'e çevirir.

Blok fiyat çarpanları maç oluşturulurken uygulanır: 1200 TRY taban fiyatla `KALE` bloğu (×0.75) 900,
`VIP` (×3) 3600 olur.

## Çalıştırma

.NET 10 SDK ve bir container çalışma ortamı (Docker Desktop veya Podman) gerekir.

```powershell
dotnet run --project orchestrator/StadiaPass.AppHost
```

Aspire; PostgreSQL (pgAdmin ile), Redis (RedisInsight ile), RabbitMQ (management eklentisiyle), Keycloak,
Vault, API ve MVC uygulamasını ayağa kaldırır. İlk açılışta şema oluşturulur ve iki mekân, üç maç (742
koltuk) ile doldurulur; Keycloak realm'i içe aktarılır.

| Kaynak | Varsayılan yerel adres |
|---|---|
| MVC arayüzü | http://localhost:5230 |
| Keycloak | https://localhost:8080 |
| Vault arayüzü | http://localhost:8200 |
| RabbitMQ management | Aspire panosunda `messaging` kaynağının üzerinde görünür |
| API | http://localhost:5042 |
| API referansı (Scalar) | http://localhost:5042/scalar/v1 |
| OpenAPI dokümanı | http://localhost:5042/openapi/v1.json |
| Health | http://localhost:5042/health |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 |

Aspire panosu Scalar referansını `webapi` kaynağının üzerinde bir link olarak gösterir. OpenAPI dokümanı ve
Scalar yalnızca Development ortamında map edilir.

## API yüzeyi

| Metot | Rota | Gereken izin |
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
| `POST` | `/api/v1/tickets` | `StadiaPass.Tickets.Purchase` (önce kartı çeker, sonra bileti keser) |
| `GET` | `/api/v1/tickets/mine` | `StadiaPass.Tickets.View` |
| `GET` | `/api/v1/tickets/{id}` | `StadiaPass.Tickets.View` (kendi bileti) / `StadiaPass.Tickets.ViewAll` (herkesinki) |

`GET /api/v1/tickets/{id}` tek başına bir izinle güvenceye alınamaz: her müşteri `Tickets.View` taşır, çünkü
kendi biletini açmak bunu gerektirir. Bu yüzden handler ayrıca bilet sahibini de kontrol eder ve başkasının
bileti için 403 değil **404** döner — "yasak" cevabı, tahmin edilen bir id'nin gerçek olduğunu doğrulardı.
Gişenin önündeki bileti sorgulayabilmesini sağlayan şey `Tickets.ViewAll`'dır.

`GET /api/v1/matches/{id}/seats` haritayı bloğa ve sıraya göre gruplanmış olarak döner; bir koltuk seçicinin
çizim için ihtiyaç duyduğu tam olarak budur:

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

Hatalar `ProblemDetails` olarak döner: `400` doğrulama, `404` bulunamadı, `409` çakışma, `422` domain kuralı
ihlali.

## Yetkilendirme

Kimlik doğrulama Keycloak'a devredilmiştir; yetkilendirme ise **izin tabanlı ve tamamen dinamiktir**. Kodun
hiçbir yerinde bir rol adı geçmez.

```
Keycloak realm rolü  ──►  KeycloakPermissionClaimsTransformation  ──►  "permission" claim'i
   "StadiaPass.Tickets.Purchase"       (StadiaPassPermissions'a karşı filtrelenir)
                                              │
        .RequireAuthorization(StadiaPassPermissions.Tickets.Purchase)
                                              │
                              PermissionPolicyProvider  ──► policy'yi ihtiyaç anında üretir
                                              │
                              PermissionAuthorizationHandler  ──► 200 / 403
```

- Bir izin string'inin tanımlandığı tek yer `StadiaPassPermissions`'dır (SharedKernel); `All`, iç içe
  sabitleri üzerinde reflection ile keşfedilir ve bir `FrozenSet` ile desteklenir.
- `PermissionPolicyProvider`, bilinen herhangi bir izin ilk kez istendiğinde ona bir `AuthorizationPolicy`
  üretir; böylece `AddPolicy(...)` elle hiç yazılmaz.
- Uygulamanın tanımlamadığı roller, güvenilmek yerine claims transformation tarafından atılır; yani
  Keycloak'ta bir rol eklemek erişimi sessizce genişletemez.
- MVC uygulaması aynı dönüşümü çalıştırır, dolayısıyla `User.HasPermission(...)` API'nin zaten reddedeceği
  eylemleri gizler — admin menüsü ve koltuk butonları bir müşteriye hiç render edilmez.
- İzin eklemek iki adımlık bir değişikliktir: sabiti ekle, karşılığındaki realm rolünü ekle.

### Oturum ve token ömrü

Keycloak yarım saat geçerli bir access token verir; onu taşıyan oturum ise saatlerce sürer. MVC uygulaması bu
token'ı API'ye yaptığı her çağrıda tekrar oynatır, dolayısıyla ikisinin senkron tutulması gerekir — aksi
halde kullanıcı giriş yapmış görünmeye devam ederken tıkladığı her şey 401 dönerdi.
`TokenRefreshingCookieEvents` her istekte cookie'yi doğrular ve token'ın bitmesine iki dakika kala refresh
token'ı yenisiyle takas eder; yenilenen ticket Redis'e geri yazılır. Keycloak reddederse — refresh token
kullanılmış, iptal edilmiş ya da oturum karşı tarafta kapanmışsa — yerel oturum da yarı canlı bırakılmak
yerine sonlandırılır.

### Yönetici ile müşteri

| | Yönetici (`Matches.Create`, `Venues.*`) | Müşteri (`Tickets.Reserve`, `Tickets.Purchase`) |
|---|---|---|
| MVC | `/Admin/Match/Create` formu, menüde "Create match" | maç listesi, koltuk seçici, biletlerim |
| API | mekân tanımlama, maç oluşturma | koltuk haritası okuma, koltuk tutma, satın alma |

### Scalar üzerinden test

OpenAPI dokümanı bir OAuth2 authorization code akışı yayımlar; bu yüzden Scalar referansı Keycloak'a
yönlendiren bir **Authorize** butonu render eder (PKCE `S256`, public client `stadiapass-scalar`) ve token'ı
her isteğe enjekte eder. Korumalı her operasyon, ihtiyaç duyduğu izinle işaretlenmiştir.

### Roller ve demo kullanıcılar (realm import, yalnızca geliştirme)

İş rolleri realm ile birlikte composite rol olarak gelir; böylece sıfırdan bir başlangıçta çalışan bir izin
matrisi hazır bulunur. Portalda düzenlemek realm dosyasını değil Keycloak'ı değiştirir.

| Rol | İzinler |
|---|---|
| `Administrator` | kimlik portalı dahil her şey |
| `MatchManager` | mekânlar, maç oluşturma ve erteleme, bilet okuma |
| `BoxOffice` | maç okuma, başkalarınınki dahil bilet okuma, tutma, satın alma ve iptal |
| `Customer` | maç okuma, bilet okuma, tutma ve satın alma |
| `Viewer` | yalnızca maç ve bilet okuma |

| Kullanıcı | Parola | Rol |
|---|---|---|
| `mudur` | `mudur` | `Administrator` |
| `organizator` | `organizator` | `MatchManager` |
| `gise` | `gise` | `BoxOffice` |
| `musteri` | `musteri` | `Customer` |
| `seyirci` | `seyirci` | `Viewer` |

Bir düzine ayrı izin rolü yerine tek bir composite rol atamak, üretilen token'ları da küçük tutar — ki bu
önemli, çünkü buradaki her şey aynı `localhost` cookie kavanozunu paylaşıyor.

Keycloak https://localhost:8080 adresinde çalışır ve realm her açılışta yeniden içe aktarılır (data volume
yok), dolayısıyla gerçeğin kaynağı realm dosyasıdır. `stadiapass-realm.json` içindeki MVC client secret'ı
yerel bir geliştirme değeridir — herhangi bir dağıtımdan önce gerçek bir sır deposuyla değiştirin.

## Anonim gezinme

Gezinme anonimdir, bir biletleme sitesinin çalışma şekli budur. Ziyaretçi fikstüre düşer, bir maçı açar ve
koltuk haritasının dolmasını hesapsız izler; yalnızca koltuk tutmak veya satın almak giriş ister.

| Erişim | Anonim | Giriş yapmış |
|---|---|---|
| `GET /api/v1/matches` | evet | evet |
| `GET /api/v1/matches/{id}/seats` | evet | evet |
| koltuk tutma, satın alma, back office | hayır | uygun izinle |

Misafir olarak boş bir koltuğa tıklamak API'ye hiç ulaşmaz. Koltuk sıradan bir buton olarak render edilir ve
sayfa tıklamayı, dönüş adresinde koltuğu taşıyan bir giriş turuna çevirir:

```
/Matches/SeatSelection/{id}              misafir GUNEY-1-1 koltuğuna tıklar
  -> /Account/Login?returnUrl=/Matches/SeatSelection/{id}?seat=GUNEY-1-1
  -> Keycloak girişi
  -> aynı koltuk haritasına dönüş, "Hold this seat GUNEY-1-1" teklifiyle
```

Navigasyon misafire **Log in** ve **Register**, aksi halde giriş yapmış kullanıcı adını gösterir. Register,
OIDC challenge'ını Keycloak'ın kayıt endpoint'ine yönlendirir; böylece state, nonce ve PKCE sahipliği
handler'da kalır. Yeni hesaplar realm'in varsayılan grubuna düşer, o grup da `Customer` rolünü taşır —
dolayısıyla yeni kaydolan biri hemen satın alabilir.

## Kimlik portalı

Roller ve kullanıcılar StadiaPass veritabanında **tutulmaz**. API her değişikliği `IKeycloakAdminService`
üzerinden Keycloak Admin REST API'sine aracılık eder; bunu `stadiapass` realm'iyle sınırlı bir servis hesabı
ile yapar — master realm yönetici parolası uygulamaya hiç ulaşmaz.

```
WebMVC portalı  ->  WebAPI  ->  IKeycloakAdminService  ->  Keycloak Admin REST API
  checklist arayüzü   MediatR      servis hesabı token'ı     /admin/realms/stadiapass/...
```

- Bir **iş rolü**, Keycloak'ta composite bir realm rolüdür; checklist'te işaretlenen izinler onun
  composite'leri olur, böylece üyenin token'ı o izin string'lerinin hepsini taşıyacak şekilde genişler.
- Checklist `StadiaPassPermissions.Groups`'tan render edilir; shared kernel'e bir sabit eklemek onu hiçbir
  arayüz değişikliği olmadan portalda görünür kılar. Eksik izin rolleri Keycloak'ta ihtiyaç anında yaratılır.
- İzin rolleri silinemez ve bir iş rolü adı olarak yeniden kullanılamaz — portal ikisini de engeller.
- Keycloak'ın kendi rolleri (`offline_access`, `uma_authorization`, `default-roles-*`) filtrelenir.

| Metot | Rota | Gereken izin |
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

Portal ekranları `/Admin/Roles` ve `/Admin/Users` altındadır ve yalnızca giriş yapmış kullanıcı
`Roles.Manage` veya `Users.Manage` taşıyorsa navigasyonda görünür.

### Oturum depolama

Bir yönetici çok sayıda rol taşır, bu da OIDC token'larını — dolayısıyla authentication ticket'ını — büyütür.
MVC uygulaması ticket'ı bir `ITicketStore` arkasında Redis'te tutar ve cookie'de yalnızca bir oturum anahtarı
bırakır; böylece kullanıcının kaç rolü olursa olsun giriş çalışır ve çıkış oturumu gerçekten iptal eder.

## Loglama

Her iki uygulamanın da loglama hattının sahibi Serilog'dur. Kurulum bir kez, `AddServiceDefaults` içinde
yapılır; böylece yeni bir servis ServiceDefaults'a referans vererek aynı konfigürasyonu alır — bir
`Program.cs`'te hatırlanması gereken `UseSerilog()` çağrısı ve zamanla sapabilecek ikinci bir kayıt yoktur.

```
ILogger<T>  (Microsoft.Extensions.Logging API'si - çağrı yerleri Serilog'dan hiç bahsetmez)
  └── Serilog
        ├── Console          insan okuyabilir, invariant culture
        └── OpenTelemetry    OTLP -> Aspire panosu, yapısal öznitelikler korunarak
```

Her olay `ApplicationName`, `Environment` ve `ThreadId` taşır; bir HTTP isteği sürerken ayrıca
`CorrelationId` ile Keycloak token'ından çözülen `UserId` ve `UserName` de eklenir. Correlation id, çağıran
taraf gönderdiyse `X-Correlation-ID` başlığından, aksi halde mevcut `Activity`'den alınır — böylece bir log
satırı ile dağıtık bir trace aynı id'yi gösterir.

MediatR `LoggingBehavior`'ı komutun ya da sorgunun kendisini, destructure edilmiş halde Serilog'un
`LogContext`'ine iter. Handler'ın içinde herhangi bir yerde üretilen her olay — handler'ın kendi yazdıkları
dahil — bu sayede onu doğuran parametreleri ve isteyen kişiyi taşır:

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

`CreateUserCommand` bir parola taşıyor; toptan destructuring'i tehlikeli yapan da tam olarak bu.
ServiceDefaults'taki bir destructuring policy, olay yazılmadan önce adında `password`, `secret`, `token`,
`credential`, `apikey` veya `accesscode` geçen her üyeyi maskeler — böylece ileride bir komut yenisini
eklese bile bir kimlik bilgisi hiçbir sink'e ulaşamaz.

Gürültü bilinçli olarak dışarıda tutulur — kimsenin okuyamadığı log, kimsenin okumadığı logdur:

| Kaynak | Seviye | Neden |
|---|---|---|
| `Microsoft.*`, `System.*` | Warning | framework her isteği üç kez anlatıyor |
| `Microsoft.EntityFrameworkCore.Database.Command` | Warning | aksi halde her SQL ifadesi, Information seviyesinde |
| `Polly` | Warning | Keycloak'a giden her çağrının her denemesi için bir satır |
| `/health`, `/alive`, `/metrics` | Verbose | Prometheus 5 sn'de bir topluyor; bunlar logu ele geçirirdi |

`UseSerilogRequestLogging`, framework'ün istek başına üç satırını; metot, rota, durum kodu ve süre taşıyan
tek bir satırla değiştirir. API'de en dıştaki middleware'dir, böylece satır çağıranın gerçekten aldığı durumu
raporlar; MVC'de statik dosya handler'ından sonra durur, böylece bir sayfa görüntüleme bir düzine değil tek
satır kalır.

Her iki `Program.cs` de host var olmadan önce bir bootstrap logger yaratır; böylece açılış sırasındaki bir
hata — bozuk bir connection string, ulaşılamayan bir Keycloak — süreçle birlikte kaybolmak yerine yazılır.

## Metrikler ve dashboard'lar

`StadiaPass.ServiceDefaults` zaten OpenTelemetry metriklerini topluyor ve OTLP üzerinden Aspire panosuna
gönderiyordu. Aynı meter'lar artık bir Prometheus scrape endpoint'inde de yayımlanıyor, böylece pull tabanlı
bir yığın onları araya bir collector koymadan okuyabiliyor.

```
ServiceDefaults meter'ları
  ├── OTLP push  ──►  Aspire panosu             (canlı, çalışma başına)
  └── /metrics   ◄──  Prometheus (5 sn scrape)  ──►  Grafana
```

| Kaynak | URL | Notlar |
|---|---|---|
| Prometheus | http://localhost:9090 | API ve MVC uygulamasını 5 sn'de bir toplar |
| Grafana | http://localhost:3000 | geliştirmede anonim admin, aksi halde `admin` / `admin` |
| Scrape endpoint | http://localhost:5042/metrics | ServiceDefaults yayımlar, yalnızca Development |

Her iki container da `orchestrator/StadiaPass.AppHost/monitoring` altındaki dosyalardan provision edilir;
böylece taze bir klon, data source'u bağlı ve dashboard'u **StadiaPass** klasöründe hazır olarak açılır:

```
monitoring
├── prometheus/prometheus.yml                     # webapi ve webmvc için scrape job'ları
└── grafana
    ├── provisioning/datasources/prometheus.yml   # data source, otomatik kaydedilir
    ├── provisioning/dashboards/dashboards.yml    # dosya sağlayıcı
    └── dashboards/stadiapass-runtime.json        # dashboard'un kendisi
```

Uygulamalar host üzerinde çalışırken Prometheus bir container'da çalışıyor, dolayısıyla scrape hedefleri
`host.docker.internal:5042` ve `:5230`. Grafana, Prometheus'a paylaşılan container ağı üzerinden Aspire
kaynak adıyla ulaşır; bu da data source'u Aspire'ın hangi host portunu yayımladığından bağımsız kılar.

**StadiaPass runtime** dashboard'u, exporter'ın gerçekten ürettiği metrik adlarına göre yazıldı:

| Panel | Sorgu |
|---|---|
| Saniyedeki istek, p95 süre, 5xx oranı | `http_server_request_duration_seconds_*` |
| Rotaya göre süre | `http_route` üzerinde `histogram_quantile` |
| Working set ve GC heap | `dotnet_process_memory_working_set_bytes`, `dotnet_gc_last_collection_heap_size_bytes` |
| Allocation hızı ve GC duraklamaları | `dotnet_gc_heap_total_allocated_bytes_total`, `dotnet_gc_pause_time_seconds_total` |
| CPU ve thread pool | `dotnet_process_cpu_time_seconds_total`, `dotnet_thread_pool_thread_count_total` |
| Exception'lar ve lock contention | `dotnet_exceptions_total`, `dotnet_monitor_lock_contentions_total` |
| PostgreSQL komut süresi | `db_client_operation_duration_seconds_bucket` |

## Sırlar

Bu depoda hiçbir sır yazılı değildir. Veritabanı parolası, Keycloak client secret'ları ve Stripe anahtarı
**HashiCorp Vault**'ta durur; bir uygulamaya verilen tek şey bir adres ve bir token'dır.

```
AppHost                          Vault (KV v2)                 WebAPI / WebMVC
  yalnızca kendi bildiğini    ──►  secret/stadiapass  ──►  IConfiguration
  çözer (üretilen portlar,                                 (en son eklenir, o yüzden kazanır)
   parolalar)
```

`AddVaultConfiguration()` her iki `Program.cs`'in de ilk satırıdır, herhangi bir şey konfigürasyonu okumadan
önce — bir connection string, container inşa edilirken çözülür, dolayısıyla sonradan eklenen bir kaynak ona
ihtiyaç duyan şeyden sonra gelirdi. Sıradan bir `ConfigurationProvider` kaydeder; kod tabanının geri kalanını
Vault'un varlığından habersiz tutan da budur: `IOptions<T>`, `GetConnectionString` ve diğer her şey aynen
eskisi gibi çalışmaya devam eder.

| Vault'taki anahtar | Kullanan |
|---|---|
| `ConnectionStrings:stadiapassdb` | EF Core |
| `ConnectionStrings:cache` | Redis cache ve MVC ticket store |
| `ConnectionStrings:messaging` | MassTransit / RabbitMQ |
| `Keycloak:AdminClientSecret` | `KeycloakAdminService` servis hesabı |
| `Keycloak:ClientSecret` | MVC OpenID Connect girişi |
| `PaymentProvider:Type`, `PaymentProvider:SecretKey` | ödeme sağlayıcı stratejisi |

### Fallback yok

Sır taşıyan option'ların **varsayılan değeri yoktur** ve `ValidateOnStart` ile `[Required]` işaretlidir.
Çalışan bir varsayılanı olan sır, biri onu ayarlamayı unuttuktan sonra da sessizce çalışmaya devam eden ve
sonra üretime kadar giden sırdır. API'yi Vault olmadan başlatın, anında durur:

```
OptionsValidationException: DataAnnotation validation failed for 'KeycloakAdminOptions' members:
'AdminClientSecret' with the error: 'Keycloak:AdminClientSecret is not set. It is expected to come from Vault.'
```

### Geliştirme

`Aspire.Hosting.Vault` diye bir paket olmadığı için AppHost resmi imajı doğrudan orkestre eder: dev modda,
bellekte ve mühürsüz bir sunucu, root token'ı `stadiapass-root-token`. Vault hazır olduğunu bildirdiğinde
AppHost yalnızca kendisinin çözebileceği değerleri — Aspire'ın Postgres, Redis ve RabbitMQ için ürettiği
portlar ve parolalar — yazar, Stripe anahtarını da kendi ortamından geçirir:

```powershell
$env:PaymentProvider__Type = "Stripe"
$env:PaymentProvider__SecretKey = "sk_test_..."
dotnet run --project orchestrator\StadiaPass.AppHost
```

Arayüz, Aspire panosunda **Vault UI** olarak durur (http://localhost:8200), token `stadiapass-root-token`.
Yeniden başlatmadan hiçbir şey sağ çıkmaz, ki amaç da budur: kalıcı olan bir geliştirme sır deposu, er ya da
geç içinde gerçek bir şey tutan sır deposudur.

### Dağıtıma taşırken

Üç şey değişir, hiçbiri uygulama kodunda değil:

1. Dev container'ı gerçek bir Vault kümesine dönüşür ve `Vault__Address` ona bakar.
2. `Vault__Token` artık bir root token olmaz. Vault kapsamı daraltılmış birini AppRole, Kubernetes auth ya da
   agent sidecar üzerinden verir; provider token'ı nasıl elde edildiğine bakmadan kullanır.
3. Vault'u orkestratör beslemez olur — gerçeğin kaynağı Vault'tur, AppHost'un besleme adımı yalnızca bir
   klonun çalışır halde açılması için vardır.

## Eşzamanlılık

İki kişi aynı koltuğu istiyor. Aşağıdaki her şey bu tek cümle için var.

### Koltuk: PostgreSQL `xmin`

İki istek de koltuğu okur, ikisi de onu satın alınabilir bulur, ikisi de yazar. Bir koruma olmadan ikinci
yazma birincinin üstüne oturur: tek koltuk, iki bilet, aynı turnikede iki kişi.

PostgreSQL zaten her satırı, onu en son yazan transaction'ın id'siyle damgalıyor — `xmin` adlı gizli bir
sistem sütununda. Bunu concurrency token olarak maplemek her UPDATE'i koşullu hale getirir:

```sql
UPDATE stadiapass.match_seats SET "HolderReference" = @p0, "Status" = @p1
WHERE "Id" = @p2 AND xmin = @p3
```

İkinci yazan sıfır satır eşleştirir ve EF Core `DbUpdateConcurrencyException` fırlatır. Ne fazladan bir
kolon, ne migration, ne de bir istek boyunca tutulan kilit var — ki bu burada önemli, çünkü bu proje şemasını
`EnsureCreated` ile oluşturuyor ve gerçek bir versiyon kolonu, hâlihazırda var olan bir veritabanına asla
ulaşmazdı.

`DbUpdateConcurrencyException`, handler'da değil `UnitOfWork`'te `ConcurrencyConflictException`'a çevrilir:
application katmanı EF Core'a referans vermez, ve onu bu halde tutan dikiş tam olarak budur.

### Sayaçlar: göreli güncelleme

Token bir koltuğu korur, maçı değil. Aynı maçın **farklı** iki koltuğunu alan iki kişi asla aynı koltuk
satırına dokunmaz — ama ikisi de okudukları koltuk sayılarını geri yazar ve iki satıştan biri toplamlardan
sessizce kaybolur.

Aggregate kendi sayaçlarını bellekte hâlâ hareket ettiriyor, çünkü domain kurallarını ve onları sabitleyen
testleri dürüst tutan şey bu. O değerler yalnızca veritabanına hiç ulaşmıyor. Değişmemiş olarak işaretleniyor
ve toplamları PostgreSQL hesaplıyor:

```sql
UPDATE stadiapass.matches AS m
SET "ReservedSeatCount" = m."ReservedSeatCount" - 1,
    "SoldSeatCount"     = m."SoldSeatCount" + 1,
    "Status" = CASE WHEN m."AvailableSeatCount" = 0 AND m."ReservedSeatCount" = 1
                    THEN 'SoldOut' ELSE m."Status" END
WHERE "Id" = @match_Id
```

Sold-out kontrolü, sayının zaten sıfır olup olmadığını değil, satılmakta olan rezervasyonun **sonuncusu**
olup olmadığını sorar; çünkü her `SET` ifadesi satırı bu ifadeden önceki haliyle okur. Bu, transaction'ın
içinde ilk çalışan şeydir, koltuğa dokunulmadan önce: en kaba satırı alır, böylece aynı maçın eşzamanlı
satışları burada sıraya girer — iki satıra ters sırayla uzanmak yerine, ki deadlock tam olarak öyle doğar.

### Kapı: bir Redis kilidi

Token çift satışı imkânsız kılıyor, ama bunu ancak en sonda söylüyor — o noktaya gelindiğinde kaybeden
isteğin kartı zaten çekilmiş ve hiç alamayacağı bir koltuk için iade edilmiş oluyor. Birinin ekstresinde,
hiçbir şey karşılığında bir çekim ve bir iade.

Bu yüzden istek onun yerine kapıda çevriliyor — koltuk haritası okunmadan ve Stripe çağrılmadan önce:

```
SET lock:seat:{matchId}:{seatNumber} <token> NX PX 60000
```

Bu tek satırdaki üç karar, satırın kendisinden daha değerli.

**Serbest bırakma bir silme değil, karşılaştır-ve-sil.** Bir lease dolduktan ve anahtarı başkası aldıktan
sonra düz bir `DEL`, *onun* kilidini çöpe atar ve üçüncü bir çağıranı onun yanına sokar. Saklanan değer tek
kullanımlık bir token'dır ve serbest bırakma bunu tek bir script içinde sorar, çünkü Redis bir script'i araya
başka hiçbir şey sokmadan çalıştırır:

```lua
if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end
```

**Lease bir dakika, rezervasyon penceresinin on dakikası değil.** Tutma müşteriye verilmiş bir sözdür; bu
lease'in ise yalnızca tek bir ödeme denemesinden uzun yaşaması gerekir. Pencereye eşitleyin, satın alma
ortasında ölen bir süreç koltuğu on dakika alınamaz hale getirir — üstelik hâlâ rezervasyonu elinde tutan
kişiye de, ki o beklerken tutması dolar.

**Redis'e ulaşılamıyor olması bilet satmayı durdurmak için bir sebep değildir.** Kilit bir uyarıyla açık
başarısız olur (fail open), çünkü doğruluk koltuğun concurrency token'ında yaşar, burada değil. Bir cache
kesintisi bir satış kesintisine dönüşmemeli. Bu yüzden `null` her zaman *başkası tutuyor* demektir, asla
*kilide ulaşılamadı* değil.

> Bu kilit, veritabanının zaten sağlamadığı hiçbir güvenlik eklemiyor. Eklediği şey, **yapılmayan iş**:
> koltuk haritası okuması yok, çekim yok, iade yok, ve bir ekstrede kafa karıştıran bir çift satır yok.

## Ödemeler

Bir koltuk, ancak arkasındaki kart çekildikten sonra satılır. Application katmanı tek bir port tanır,
`IPaymentService`; hangi adaptörün cevap vereceği bir konfigürasyon satırıdır, dolayısıyla checkout'un tamamı
— reddedilme yolu dahil — Stripe hesabı, anahtarı ve ağı olmayan bir dizüstünde çalışır.

```
ConfirmTicketPurchaseCommandHandler
  ├── 1. IDistributedLock.TryAcquireAsync("lock:seat:{match}:{seat}")   biri ödeme ortasındaysa 409
  ├── 2. match.EnsureSeatCanBeSoldTo(...)   satışın her kuralı, henüz hiçbir şey değişmedi
  ├── 3. IPaymentService.ProcessPaymentAsync(...)   ──► Mock  (yerel)
  │                                                 └─► Stripe (test API'si)
  ├── 4. match.ConfirmSeatSale(...) + Ticket.IssueFor(...)
  └── 5. tek transaction ─┬── maç satırında atomik sayaç güncellemesi
                          ├── koltuk + bilet, koltuğun xmin'i ile korunarak
                          └── TicketPurchasedEvent, outbox'a
                              └── hata halinde: RefundPaymentAsync, ardından 409
```

Sıralamanın kendisi tasarımın tamamıdır. Kurallar karta dokunulmadan **önce** çalışır, çünkü birini çekip
ardından tutmasının dolmuş olduğunu keşfetmek onu ödemiş ve koltuksuz bırakır. Çekim başarılı olana kadar
hiçbir şey yazılmaz, dolayısıyla bir ret telafi gerektirmez: koltuk, tutma süresi dolana kadar o müşteri için
hâlâ `Reserved`'dır ve başka bir kart deneyebilir. Ret, sağlayıcının koduyla birlikte **422** olarak döner:

```json
{ "title": "Payment declined", "status": 422,
  "detail": "The card has insufficient funds.", "paymentFailureCode": "insufficient_funds" }
```

### Para gitti ama satış olmadı

3. ve 5. adım tek bir atomik eyleme dönüştürülemez: kart internetin öbür ucundaki bir şirkette çekilir,
koltuk ise buradaki bir veritabanında satılır. İkisinin arasında satış hâlâ başarısız olabilir — koltuk
satıra önce ulaşan birine gidebilir ya da bağlantı kopabilir. Müşteri ödemiş ve koltuksuz kalırdı; kod
yazarak önlemeye değer tek sonuç budur.

Bu yüzden 5. adımdan başarı dışında çıkan her yol, exception daha ileri gitmeden önce parayı geri verir ve
ancak ondan sonra **409** cevaplar. İki ayrıntı bunu iyi niyetli olmaktan çıkarıp güvenli yapar:

- **İadeye isteğin cancellation token'ı verilmez.** O noktada müşteri sekmeyi çoktan kapatmış olabilir, ve bu
  parasını tutmak için bir sebep değildir. `CancellationToken.None` ile çalışır.
- **İade bir idempotency key taşır**: `refund:{transactionId}`. Böylece tekrar denenen bir girişim parayı bir
  kez geri verir. Stripe tekrara ikinci bir iade üretmek yerine aynı refund id'sini döner.

Çekim de aynı şekilde anahtarlanır — `stadiapass:{matchId}:{seatNumber}` — çift tıklamayı iki değil tek
çekim yapan şey budur.

Kendisi başarısız olan bir iade fırlatılmaz, sağlayıcının transaction id'siyle `Error` seviyesinde loglanır:
çağıran zaten bir hataya doğru gidiyordur ve o hatayı bununla değiştirmek asıl neyin ters gittiğini gizlerdi.
O log satırı, parayı elle geri vermek için bir insanın ihtiyaç duyduğu şeydir.

> **Bu atomiklik değil, telafidir.** Hiçbir şey bir çekim ile bir veritabanı yazmasını birlikte commit
> ettiremez. Burada iddia edilen daha dar ve test edilebilir: para hiçbir zaman satılmamış bir koltuk için
> alınmış kalmaz, ve o geri almanın hiçbir tekrarı parayı iki kez almaz ya da iki kez geri vermez.

### Sağlayıcı seçimi

Her iki değer de **Vault**'tan gelir, `PaymentProvider:Type` ve `PaymentProvider:SecretKey` altında — oraya
nasıl gittiklerini ve nasıl değiştirileceğini [Sırlar](#sırlar) bölümünde bulabilirsiniz. Uygulamadaki hiçbir
şey değerlerin nereden geldiğini bilmez: sağlayıcı stratejisi sıradan konfigürasyon okur.

Yalnızca test anahtarı kabul edilir. Bir `sk_live_` anahtarı bir geliştirme makinesinden gerçek kartları
çekerdi, bu yüzden ilk checkout'ta değil **açılışta** reddedilir.

| `Type` | Adaptör | Davranış |
|---|---|---|
| `Mock` (varsayılan) | `MockPaymentService` | süreçten hiç çıkmaz; `4242…` kabul edilir, `4000…` yetersiz bakiye döner, geri kalan her şey reddedilir |
| `Stripe` | `StripePaymentService` | Stripe'ın test API'sinde bir PaymentIntent oluşturup onaylar, koltukla anahtarlanmış olarak — böylece çift tıklama ikinci bir çekim olmaz |

Anahtarsız bir `Stripe` sağlayıcısı ya da `sk_test_` olmayan bir anahtar, ilk checkout'ta değil **açılışta**
hata verir — bir canlı anahtar, geliştirme makinesinden gerçek kartları çekerdi.

### Karta ne oluyor

Hiçbir şey saklamıyor. Bilgiler formdan sağlayıcıya gider ve istekle birlikte kapsam dışına çıkar: kolon yok,
cache yok, redirect boyunca taşınan TempData yok. Komuttaki `CardNumber` ve `Cvv`, herhangi bir olay
yazılmadan önce log destructuring policy'si tarafından maskelenir; `PaymentCard` ise bir şey onu yanlışlıkla
yazdığında `**** **** **** 4242` olarak render edilir:

```json
"Request": { "SeatNumber": "GUNEY-1-3", "CardHolderName": "FURKAN PASAOGLU",
             "CardNumber": "***redacted***", "Cvv": "***redacted***" }
```

Numara, herhangi bir sağlayıcı çağrılmadan önce Luhn kontrol hanesine karşı doğrulanır — böylece bir yazım
hatası, müşterinin ekstresinde bir ret yerine tarayıcıya bir gidiş dönüşe mal olur.

### Stripe adaptörü kartı neden hiç göndermiyor

Stripe, hesap özel olarak onaylanmadıkça bir sunucudan gelen ham kart numarasını reddeder — *"Sending credit
card numbers directly to the Stripe API is generally unsafe"*. Bu ret doğru bir rettir, adaptör de ona uyar:
`StripeTestCards`, Stripe'ın yayımladığı test numaralarının her birini onu temsil eden payment method
token'ına eşler ve yalnızca token gönderilir. Bunlardan biri olmayan bir kart, reddedilmek üzere Stripe'a
itilmek yerine yerel olarak ve bir açıklamayla cevaplanır.

| Kart | Sonuç |
|---|---|
| `4242 4242 4242 4242` | kabul |
| `4000 0000 0000 9995` | `insufficient_funds` |
| `4000 0000 0000 0002` | `generic_decline` |
| `4000 0000 0000 0069` | `expired_card` |
| `4000 0000 0000 0127` | `incorrect_cvc` |

> **Bu hâlâ bir üretim entegrasyonu değil.** Kart, token'a dönüştürülmeden önce bu sunucuya ulaşıyor; bu da
> gerçek bir dağıtımı PCI DSS kapsamına sokar. Üretimde kartı tarayıcı Stripe.js veya Elements ile token'a
> çevirir ve sunucu yalnızca bir payment method id görür — adaptörün zaten çalıştığı şeklin ta kendisi,
> dolayısıyla port değişmez: yalnızca token'ın nereden geldiği değişir.

## Mesajlaşma

Bilet satmak, müşterinin isteğinin bittiği ve başka birkaç şeyin başladığı yerdir: bileti üret, onay
gönder, bilmek isteyen ne varsa güncelle. Bunların hiçbirinin bir checkout'u yavaşlatabilmesi, hiçbirinin de
bir checkout'u düşürebilmesi gerekmiyor.

Akla ilk gelen düzenin — satışı commit et, sonra event yayınla — tam ortasında bir boşluk var. Süreç ikisinin
arasında durabilir, ve o zaman satılmış bir bilet olur ama kimseye haber verilmemiştir: bilet yok, mail yok,
ve sistemde bunun eksik olduğunu bilen hiçbir şey yok. Önce yayınlamak daha kötüsü: geri alınabilecek bir
satışı duyurmuş olur.

### Outbox

Bu yüzden mesaj hiç yayınlanmıyor. **Satışla aynı transaction'a yazılıyor** ve ikisi tek bir kaderi
paylaşıyor:

```
ConfirmTicketPurchaseCommandHandler
  └── UnitOfWork.ExecuteInTransactionAsync
        ├── sayaçlar        (göreli güncelleme)
        ├── koltuk + bilet  (xmin ile korunarak)
        └── outbox_messages satırı   ◄── TicketPurchasedEvent, JSON olarak
                    │
                    │  OutboxProcessor, 5 sn'de bir
                    ▼
              RabbitMQ ──► ticket-purchased-event ──► TicketPurchasedEventConsumer
```

| Kolon | |
|---|---|
| `id` | UUIDv7, böylece satırlar kabaca gerçekleşme sırasına göre yazılır |
| `occurred_on_utc` | sıralama, ve sweeper'ın okuduğu kısmi index |
| `type` | tam tip adı; içeriğin nasıl geri okunacağını söyleyen şey budur |
| `content` | mesajın JSON hali |
| `processed_on_utc` | broker onu alana kadar null — bu kolon kuyruğun *kendisidir* |
| `error` | son denemenin neden tutmadığı, böylece takılı kalan bir mesaj açıklanabilir |

Bir rollback mesajı satışla birlikte götürür. Çöken bir broker kayıp bir bilet değil, bir **gecikmedir**:
satır bekler, sebebi yanına yazılır ve bir sonraki süpürme tekrar dener.

### Sweeper

```sql
SELECT * FROM stadiapass.outbox_messages
WHERE processed_on_utc IS NULL
ORDER BY occurred_on_utc
LIMIT 20
FOR UPDATE SKIP LOCKED
```

`FOR UPDATE SKIP LOCKED`, API'nin ikinci bir instance'ının da bu worker'ı çalıştırabilmesini sağlayan şeydir.
Bunu beklemek yerine *bir sonraki* partiyi alır, ve asla aynı satırları almaz — bu olmasa iki instance her
mesajı iki kez yayınlardı. Parti bilerek küçük tutuldu: satırlar iş sürdüğü müddetçe kilitli kalır, ve büyük
bir parti başka bir worker'ın çoktan işleyebileceği satırları elinde tutar.

Süpürmenin tamamı `Database.CreateExecutionStrategy()` içinde çalışır. Aspire'ın Npgsql component'i
*yeniden deneyen* bir strategy kurar, ve yeniden deneyen bir strategy arkasından transaction açılmasına izin
vermez — onu tekrar oynatmasının bir yolu olmazdı. `UnitOfWork` da tam olarak aynı sebeple aynı şekle sahip.

Bir veritabanı satırından çıkan tip adı, runtime'a "bu ne ise onu getir" diye sorulmak yerine
`IntegrationEventTypes` adlı sabit bir listeye karşı çözülür. Bugün o satırlara yalnızca bu çözümdeki kod
yazıyor, ve üzerine inşa edilmemesi gereken varsayım tam olarak budur. Kayıtsız bir mesaj yazılırken
reddedilir, beş saniye sonra keşfedilmez.

### Bus

RabbitMQ üzerinde MassTransit; yayınlayan ve tüketen şimdilik aynı süreçte — ki bu, henüz parçalara
ayrılmamış bir sistemi çalıştırmanın sıradan bir yolu, ve aynı zamanda asıl mesele: mesaj gerçekten broker'a
gidiyor ve gerçekten geri geliyor, dolayısıyla bir consumer kendi servisine taşındığı gün bu taraf değişmiyor.

| | |
|---|---|
| Exchange | `StadiaPass.Application.Tickets.Events:TicketPurchasedEvent` |
| Queue | `ticket-purchased-event` (kebab-case formatter, böylece management sayfası insanların beklediği gibi okunur) |
| Paket | MassTransit **8.5.10** — son Apache-2.0 hattı, MediatR'ın 12'de sabitlenmesiyle aynı sebeple sabitlendi |

Persistence katmanı MassTransit'e hiç referans vermez. Bir `IEventBus` portu üzerinden yayınlar; onun
MassTransit adaptörü de Stripe ve Redis adaptörlerinin yanında, infrastructure katmanında durur.

> **Teslimat en az bir kezdir (at-least-once), asla tam olarak bir kez değil.** Bir mesaj broker'a ulaşıp
> onu kaydeden satır yine de commit olmayabilir, ve buna verilecek tek dürüst cevap onu tekrar göndermektir.
> Consumer'lar aynı satın almayı iki kez görüp işi iki kez yapmamalı — bileti üretmeden önce var mı diye,
> maili göndermeden önce gönderilmiş mi diye bakmalı. İki özdeş onay maili küçük bir mahcubiyettir; iki çekim
> olmazdı.
>
> Ayrıca henüz bir deneme sayacı yok, dolayısıyla asla teslim edilemeyecek bir mesaj sonsuza kadar
> denenir. Bunu kapatan şey bir kolon ve bir tavandır.

## İstek hattı

```
HTTP → Minimal API endpoint → ISender.Send(command)
     → LoggingBehavior → ValidationBehavior (FluentValidation)
     → Handler → IDistributedLock → Aggregate davranışı → IPaymentService → Repository
     → UnitOfWork.ExecuteInTransactionAsync (sayaçlar + koltuk + bilet + outbox satırı)
     → domain event'lerini süreç içinde yayınla (MediatR notification'ları)
     → OutboxProcessor → RabbitMQ → consumer'lar (hat dışında)
```

Domain event'leri süreç içinde kalır: bu transaction'ı ve onun kendi invariant'larını ilgilendirirler, ve
MediatR bunun için doğru ölçüdedir. `TicketPurchasedEvent` onlardan biri değildir — o bir entegrasyon
mesajıdır ve bir consumer'ın ihtiyaç duyduğu her şeyi taşır, böylece bize ait hiçbir veritabanına
soramayacak birine ulaşabilir.

Koltuğu tutan kişi `ICurrentUser`'dan (Keycloak subject'i) alınır, asla istek gövdesinden değil; dolayısıyla
bir müşteri başkasının adına koltuk tutamaz veya satın alamaz.

## Testler

```powershell
dotnet test StadiaPass.slnx
```

| Proje | Kapsam |
|---|---|
| `tests/StadiaPass.Domain.UnitTests` | aggregate invariant'ları: maç oluşturma, koltuk yaşam döngüsü, mekân koltuk planları |
| `tests/StadiaPass.Application.UnitTests` | portları taklit edilmiş `ReserveSeat` ve `ConfirmTicketPurchase` dilimleri: ödeme ile kalıcılığın sırası, kaybedilen koltukta iade, kapıdaki kilit, ve satış transaction'ı içine yazılan duyuru |

xUnit, NSubstitute ve FluentAssertions; `Should_X_When_Y` olarak adlandırılmış ve Arrange-Act-Assert
düzeninde yazılmış.

Domain testleri **gerçek** aggregate'ler kurar — domain'i stub'layan bir test, korumakla yükümlü olduğu kural
hakkında hiçbir şey kanıtlamaz. Zaman her yerde enjekte edilir, böylece on dakikalık tutmayı akıl yürüterek
inceleyen bir test duvar saatiyle yarışmaz. Handler testleri `IMatchRepository`, `IUnitOfWork`, `ICurrentUser`
ve `IDateTimeProvider`'ı taklit eder ama altta yine gerçek `Match` aggregate'ini sürer.

Test takımının sabitlediği şeyler:

- bir maç, kategorinin izin vermediği türde bir mekânda ya da pasif bir kategori için açılamaz
- maç oluşturmak mekân planındaki her koltuğu üretir ve her bloğun fiyat çarpanını uygular
- müsait bir koltuğu tutmak onu tam olarak `Match.ReservationWindow` kadar tutar, müsait havuzdan çıkarır ve
  `SeatReservedDomainEvent` üretir
- zaten `Reserved` ya da `Sold` olan bir koltuk reddedilir, ve reddedilen bir girişim sayaçlara dokunmaz
- süresi dolmuş bir tutma bir sonraki alıcıya devredilir; bir tutmayı satışa yalnızca tutan kişi ve yalnızca
  süresi içinde çevirebilir
- handler koltuğu `ICurrentUser`'daki çağıran için tutar, tam olarak bir kez kaydeder, ve domain reddettiğinde
  hiç kaydetmez

Bu garantiler güvenilerek değil mutasyonla sınandı: çifte rezervasyon korumasını silmek 4 domain testini,
`ICurrentUser`'dan yanlış alanı okumak 7 handler testini kırmızıya çeviriyor.

`tests/Directory.Build.props` depo ayarlarını yeniden içe aktarır ve yalnızca test koduna uygulanmayan
CA1707 ile CA2007'yi gevşetir. `StadiaPass.Application`, handler'ları tasarım gereği internal olduğu için
test projesine `InternalsVisibleTo` verir.

## Notlar

- **Migration'lar**: bu başlangıç projesi tek komutla çalışabilmek için `EnsureCreatedAsync` ve seed
  kullanıyor. Gerçek bir dağıtımdan önce
  `dotnet ef migrations add Initial -p src/Infrastructure/StadiaPass.Persistence -s src/Presentation/StadiaPass.WebAPI`
  ve `Database.MigrateAsync()`'e geçin. Şu an modeli değiştirmek `stadiapass-pgdata` volume'ünü silmek
  demek — `EnsureCreated` şemayı bir kez kurar ve bir daha ona hiç bakmaz, dolayısıyla sonradan eklenen bir
  tablo hâlihazırda var olan bir veritabanında asla belirmez. Bununla karşılaşan ilk tablo `outbox_messages`
  oldu ve initializer'da bir `CREATE TABLE IF NOT EXISTS` ile kendini istiyor. Bu geçici bir çözüm ve öyle
  de duruyor; onu ortadan kaldıracak şey migration'lardır.
- **Koltuk haritası yükleme**: `GetWithSeatAsync` filtreli bir `Include` kullanır, dolayısıyla 20 000 koltuklu
  bir mekânda koltuk tutmak tek bir satıra dokunur. Koleksiyonun tamamını yalnızca koltuk haritası ekranı
  yükler.
- **Value object'ler ve EF Core**: owned bir örnek asla iki sahip arasında paylaşılamaz. Her koltuk kendi
  `Money`'sini alır, ve bir bilet koltuk fiyatının örneğini yeniden kullanmak yerine kopyasını saklar.
- **Zaman damgaları**: Npgsql `timestamptz` için yalnızca sıfır offset'li `DateTimeOffset` değerlerini kabul
  eder, bu yüzden `Match` başlama saatini girişte UTC'ye normalize eder.
- **MediatR** `12.5.0`'a sabitlendi, son Apache-2.0 sürümü; v13+ ticari lisans gerektiriyor.
- **MassTransit** aynı sebeple `8.5.10`'a sabitlendi: v9 ticari lisansa geçti.
- **FluentAssertions** yine aynı sebeple `7.2.0`'a sabitlendi: v8'den itibaren ticari kullanım için ücretli
  lisansa geçti.
- **Aspire Keycloak entegrasyonu** (`Aspire.Hosting.Keycloak`, `Aspire.Keycloak.Authentication`) hâlâ ön
  sürüm; sabitlenen sürüm Aspire 13.5.2 SDK'sıyla eşleşiyor.
- **`stadiapass-admin-api`**, servis hesabı portalın ihtiyaç duyduğu `realm-management` rollerini taşıyan
  gizli (confidential) bir client'tır. `stadiapass-realm.json` içindeki secret'ı yerel bir geliştirme
  değeridir.
- **Culture, MVC uygulamasında invariant culture'a sabitlendi.** Model binding ve Razor render'ı, sayıları
  nokta ile ayrıştıran jQuery unobtrusive validation ile aynı fikirde olmak zorunda. Türkçe culture altında
  sunucu `500,00` render ederken istemci doğrulayıcı bunu `NaN` olarak okuyordu, ve yazılan `500.50` değeri
  `50050` olarak bağlanıyordu. Ondalık girdiler ayrıca `type="number"` olarak render ediliyor ki `step` ve
  `min` gerçekten işlesin.
- **Her şey `localhost` üzerinde çalışıyor** ve cookie'ler porta göre ayrılmıyor; dolayısıyla MVC uygulaması,
  API, Keycloak ve Aspire panosu tek bir cookie kavanozunu paylaşıyor. Yarıda bırakılan girişler eskiden
  arkalarında correlation ve nonce cookie'leri bırakıyor, istek başlığı Keycloak'ın kabul ettiğini aşana
  kadar büyüyor ve Keycloak `431` cevaplıyordu. Bu cookie'ler artık beş dakikada sona eriyor ve Keycloak
  yükseltilmiş bir başlık limitiyle çalışıyor. Yine bir `431` görürseniz çözüm `localhost` cookie'lerini
  temizlemek.
- **`stadiapass-mvc` için direct access grants açık**, böylece yerel endpoint testleri için `curl` ile token
  alınabiliyor. Dağıtmadan önce kapatın.




