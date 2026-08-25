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
│   │       │   └── Abstractions     # IPaymentService, IDistributedLock, IOutbox, IInbox, IEventBus, IEmailService, IPaymentWebhookReader
│   │       ├── Categories           # GetCategories / CreateCategory / UpdateCategory / DeleteCategory
│   │       ├── Identity             # Keycloak Admin portu: Roles / Users dilimleri
│   │       ├── Venues               # GetVenues / CreateVenue / UpdateVenue / DeleteVenue
│   │       ├── Matches              # CreateMatch / GetUpcomingMatches / GetMatchSeatMap / EventHandlers
│   │       ├── Payments             # sağlayıcı olay kontratları + ReconcilePayment / VoidPaidTicket
│   │       └── Tickets              # ReserveSeat / ConfirmTicketPurchase / GetMyTickets / GetTicketById
│   ├── Infrastructure
│   │   ├── StadiaPass.Persistence   # EF Core 10 + PostgreSQL, repository'ler, Unit of Work, seed
│   │   │   ├── Configurations       # aggregate başına IEntityTypeConfiguration
│   │   │   ├── Inbox                # InboxMessage + writer + mesajı bus'a koyan sweeper
│   │   │   ├── Matches              # tutması dolan koltukları geri veren worker
│   │   │   ├── Outbox               # OutboxMessage, writer, sweeper, derinlik metrikleri
│   │   │   └── Repositories         # Repository<T>, VenueRepository, MatchRepository, TicketRepository
│   │   └── StadiaPass.Infrastructure# yukarıdaki portların adaptörleri
│   │       ├── Email                # SMTP üzerinde MailKit + bilet onay maili şablonu
│   │       ├── Locking              # Redis SET NX PX + Lua ile compare-and-delete serbest bırakma
│   │       ├── Messaging            # RabbitMQ üzerinde MassTransit + bilet ve ödeme consumer'ları
│   │       └── Payments             # Mock ve Stripe adaptörleri, sağlayıcı stratejisi, webhook doğrulama
│   └── Presentation
│       ├── StadiaPass.WebAPI        # Minimal API - MapGroup + IEndpoint keşfi + Scalar referansı
│       │   ├── Authorization        # Keycloak JWT bağlantısı, KeycloakOptions, CurrentUser
│       │   ├── Endpoints            # Venue, Match, Ticket, Payment (webhook), Role ve User endpoint'leri
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
| `MatchSeat` | `Available` → `Reserve()` → `ConfirmSale()`, 10 dakikalık tutma, yalnızca tutan kişi satın alabilir, süresi dolan tutmalar kendiliğinden serbest kalır, ve `VoidSale()` `Sold`'dan çıkmanın tek yolu |
| `Ticket` | yalnızca maçın `Sold` durumuna geçirdiği bir koltuk için kesilebilir, kendisini ödeyen çekimi daima kaydeder, ve bir koltuk iptal edilmemiş en fazla bir bilet taşır |

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
| `POST` | `/api/v1/payments/webhook` | yok — anonim, imzayla doğrulanır |
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
| `Smtp:Host`, `Smtp:Port`, `Smtp:SenderName`, `Smtp:SenderEmail` | bilet onay maili |
| `Smtp:UserName`, `Smtp:Password` | Google hesabı ve App Password |
| `PaymentProvider:WebhookSecret` | bir webhook'un gerçekten Stripe'tan geldiğini doğrulamak |

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
portlar ve parolalar — yazar; Stripe anahtarını ve SMTP kimlik bilgilerini de kendi ortamından geçirir:

```powershell
$env:PaymentProvider__Type = "Stripe"
$env:PaymentProvider__SecretKey = "sk_test_..."
$env:Smtp__SenderEmail = "sen@gmail.com"
$env:Smtp__UserName    = "sen@gmail.com"
$env:Smtp__Password    = "xxxx xxxx xxxx xxxx"   # Google App Password, hesap parolası DEĞİL
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
olup olmadığını sorar; çünkü her `SET` ifadesi satırı bu ifadeden önceki haliyle okur.

**Ve en son çalışır, commit'ten hemen önce.** Maç satırı sistemdeki en kaba kilittir: fikstür başına tek
satır, koltuklarından herhangi birine dokunan her yazmanın istediği ve transaction commit olana kadar tutulan
satır — yani tek bir maça yapılan yazmalar orada sıraya girer. En başta alınırsa, her biri önündeki
transaction'ın koltuk yazmasını, bilet insert'ünü ve outbox insert'ünü de beklemiş olur. En sonda alınırsa
yalnızca commit boyunca tutulur. Tek maçta 24 eşzamanlı rezervasyonla ölçüldü: **80.7 ms'ye karşı 42.2 ms,
1.92 kat**.

Repository'nin güncellemeyi çalıştırmak yerine geri vermesinin sebebi de bu — `PrepareSeatSaleCounters`
sayaçları save'in elinden alır ve ifadeyi döner, çağıran da onu kaydettikten sonra çalıştırır:

```csharp
var writeCounters = matchRepository.PrepareSeatSaleCounters(match);

await unitOfWork.ExecuteInTransactionAsync(async token =>
{
    await unitOfWork.SaveChangesAsync(token);
    await writeCounters(token);
}, cancellationToken);
```

Deadlock'lar dışarıda kalır çünkü sıra her yerde aynıdır: önce koltuk satırları, en sonda maç satırı — dört
yolun hepsinde. İki transaction'ın aynı satır çiftine ters sırayla uzanması deadlock'u tam olarak böyle
doğurur; önemli olan hangisinin önce geldiği değil, üzerinde anlaşmalarıdır.

**Bunu yalnızca satış değil, dört geçişin hepsi yapar.** Rezervasyon, serbest bırakma ve void de sayaçları
tam olarak bir satış gibi hareket ettirir; bir yolun onları bellekten yazması diğer üçünün disiplinini
boşa çıkarır. İşin kötüsü aritmetiğin kendi başına kayması değil — bir yolun diğerini silmesi. Maçı okuyan,
başka bir koltuğun satışına yarışı kaybeden ve sonra okuduğu toplamları yazan bir rezervasyon, satılmış
koltuğu sanki o satış hiç olmamış gibi rezerve sütununa geri koyar:

| | `Available` | `Reserved` | `Sold` |
|---|---|---|---|
| rezervasyon maçı okur | 23 | 1 | 0 |
| başka bir koltuğun satışı commit olur | 23 | 0 | 1 |
| rezervasyon okuduğunu yazar | 22 | **2** | 1 |
| rezervasyon bunun yerine göreli güncelleme yazar | 22 | 1 | 1 |

Süresi çoktan dolmuş bir rezervasyonu devralmak, hiçbir şeyi hareket ettirmeyen tek durumdur: o koltuk zaten
rezerve olarak sayılıyor ve yalnızca el değiştiriyor. `Match.SeatsClaimedByReserving` bunu geçişten **önce**
cevaplar, çünkü sonrasında cevap her zaman sıfırdır.

### Retry: iki kez çalışan bir işlem

Aspire'ın Npgsql varsayılanları yeniden deneyen bir execution strategy'yi açık getirir ve
`IUnitOfWork.ExecuteInTransactionAsync` onun üzerinde çalışır. Geçici bir hata — kopan bağlantı, timeout,
failover — yeniden denenir ve **retry tüm delegate'i baştan çalıştırır**. Arada transaction geri alınır, yani
veritabanının yaptığı her şey geri alınır. Başka hiçbir şey alınmaz.

Bu, delegate'in sözleşmesini dar tutar: veritabanı işi içeri, geri kalan her şey dışarı. Özellikle üç şey
ikinci geçişten sağ çıkmaz:

| Delegate'in içinde bırakılırsa | Retry ne yapar |
|---|---|
| `match.VoidSeatSale(...)`, `ticket.Cancel(...)` | koltuk artık `Sold` değil, bilet artık canlı değil; ikisi de fırlatır — ağın bir anlık kesintisi, hiç uygulanmamış bir ters ibraza dönüşür |
| `outbox.Enqueue(...)` | rollback, kendisine eklenmiş olanı takipten çıkarmaz; ikinci bir kopya hazırlanır ve başarılı deneme ikisini birden kaydeder — müşteriye tek koltuk için iki onay maili gider |
| domain event'lerin save'den önce boşaltılması | save çalışsın çalışmasın aggregate'ler boşaltılır, dolayısıyla nihayet başarılı olan denemenin duyuracak hiçbir şeyi kalmaz |

Bu yüzden her handler bellekteki işini transaction **açılmadan önce** yapar, delegate'in içinde yalnızca
sayaç güncellemesi ve save kalır. Save yine tek bir save'dir, yani satış, koltuk, bilet ve mesaj hâlâ birlikte
iniyor ya da hiç inmiyor — o atomiklik zaten nesnelerin nerede hazırlandığıyla ilgili değildi.

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

**Lease sağlayıcıya sorulur, koda yazılmaz.** Yalnızca tek bir ödeme denemesinden uzun yaşaması gerekir, o
yüzden `IPaymentService.WorstCaseDuration` artı ardından gelen yazma için bir pay kadardır. Bir süre düz bir
dakikaydı ve düz bir dakika yanlıştı: Stripe adaptörünün timeout'u konfigürasyondan gelir ve SDK altında ağı
yeniden dener, dolayısıyla dürüst en kötü durum o timeout'un SDK deneme sayısı katıdır — varsayılanlarda 90
saniye, 60 saniyelik bir lease'e karşı. Koruduğu çağrı hâlâ sürerken dolan bir lease, sessizce korumayı
bırakmış bir kilittir: koltuk ödemenin ortasında açılır ve aynı koltuk için iki kart çekilir, ki kilidin
varlık sebebi tam olarak bunu engellemektir.

Bilerek rezervasyon penceresinin on dakikası da değildir. Tutma müşteriye verilmiş bir sözdür; bu ise tek bir
denemenin lease'i. Pencereye eşitleyin, satın alma ortasında ölen bir süreç koltuğu on dakika alınamaz hale
getirir — üstelik hâlâ rezervasyonu elinde tutan kişiye de, ki o beklerken tutması dolar.

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

#### Çift hata: iadenin de başarısız olması

İade telafidir; peki telafiyi ne telafi eder? Uzun süre bunun cevabı bir `Error` log satırıydı, ki bu bir
cevap değil. Sistemin **mesajlar** için tabloları, sweeper'ları, retry'ları ve ölü mektup kuyrukları vardı —
ama gerçekten parayı taşıyan tek şey kimsenin izlemediği bir yere düşüyordu. Logger okunmuyorsa ya da satır
rotasyona uğradıysa, para sağlayıcıda kalıyor ve sistemin hiçbir parçası borçlu olduğunu bilmiyordu.

Bu yüzden başarısız olan bir iade artık **yazılıyor**:

```
çekim  ->  satış patlar  ->  iade  ->  oldu mu?  ->  bitti
                                   \
                                    -> olmadı  ->  outbox'a RefundOwedEvent
                                                   -> sweeper  -> broker  -> tekrar iade
```

İlk deneme duruyor, çünkü buraya gelmenin olağan sebebi koltuk yarışını kaybetmektir, veritabanı gayet
sağlıklıdır ve paranın beş saniye sonra değil bir saniye sonra geri gitmesi daha iyidir. Değişen **ikinci**
deneme: o artık bir deneme değil, bir satır. Satır olduğu andan itibaren outbox'ın zaten yaptığı her şeyi
miras alır — yeniden başlatmadan sağ çıkar, sweeper taşır, başarısız olmaya devam ettikçe broker yeniden
teslim eder ve hiç olmazsa `stadiapass.outbox.dead` onu sayar. İade ödeme üzerinden anahtarlandığı için,
birden fazla kez teslim edilmesi parayı yine bir kez geri verir.

Satırı `IRefundLedger` yazar ve bu, adı değiştirilmiş bir `IOutbox` değildir. Çağıranı, az önce geri alınmış
satışla dolu bir change tracker tutuyordur; çağıranın unit of work'ü üzerinden hazırlamak, tam da patlayan o
satışı kaydederdi. Ledger kendi scope'unu açar — kendi context'i, kendi bağlantısı — ve kendi başına commit
eder. Hatanın **veritabanı** olduğu durumda işe yaramasını sağlayan da budur: taze bir bağlantı, zehirlenmiş
olanın çalışmadığı yerde çalışabilir.

O yazma da başarısız olursa geriye bir `Critical` log satırı kalır, başka bir şey değil — ve bu dürüst bir
sonuçtur: veritabanına erişilemiyor olması, hiçbir şeyin yazılamayacağı tek durumdur.

> **Bu atomiklik değil, telafidir.** Hiçbir şey bir çekim ile bir veritabanı yazmasını birlikte commit
> ettiremez. Burada iddia edilen daha dar ve test edilebilir: para hiçbir zaman satılmamış bir koltuk için
> alınmış kalmaz, o geri almanın hiçbir tekrarı parayı iki kez almaz ya da iki kez geri vermez, ve geri alma
> şimdi yapılamıyorsa bir insanın sorgulayabileceği bir yerde hatırlanır — izliyor olması gereken bir yerde
> değil.

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
              RabbitMQ ──► ticket-purchased-event ──► TicketPurchasedEventConsumer ──► SMTP
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

**Consumer'lar yeniden denenir, çünkü MassTransit bunu kendiliğinden yapmaz.** Tek başına
`ConfigureEndpoints` bir consumer'a tam olarak bir hak verir: bir kez fırlat, mesaj error kuyruğundadır. Bir
anlığına takılan SMTP sunucusu için bu, kimsenin almadığı bir bilet onayıdır; ters ibraz için ise parasını
geri almış birine satılı kalmış bir koltuk. Bu yüzden bus'a açık bir politika verildi — bir saniyeden otuz
saniyeye açılan beş deneme, **ondan sonra** error kuyruğu; çünkü gerçekten bozuk bir mesajın yeri, onu
gizleyen bir döngü değil, görünür bir yerdir.

> **Teslimat en az bir kezdir (at-least-once), asla tam olarak bir kez değil.** Bir mesaj broker'a ulaşıp
> onu kaydeden satır yine de commit olmayabilir, ve buna verilecek tek dürüst cevap onu tekrar göndermektir.
> Consumer'lar aynı satın almayı iki kez görüp işi iki kez yapmamalı — bileti üretmeden önce var mı diye,
> maili göndermeden önce gönderilmiş mi diye bakmalı. İki özdeş onay maili küçük bir mahcubiyettir; iki çekim
> olmazdı.
>
> Yeniden denemenin güvenli olması **tam da bundan** kaynaklanıyor. Buradaki her consumer varsaymak yerine
> veritabanına neyin doğru olduğunu sorar, ve inbox bir sağlayıcının iki kez gönderdiği her şeyi zaten
> reddetmiştir.

### Onay maili

`TicketPurchasedEventConsumer` bütün bu zincirin öbür ucu: satış commit oldu, satır süpürüldü, broker
yönlendirdi, ve mail burada hazırlanıp gönderiliyor — bir checkout'u yavaşlatamayacağı ya da düşüremeyeceği
yerde.

Mesaj, consumer'ın "bu kimdi" diye Keycloak'a sormasını beklemek yerine alıcının adresini kendisi taşıyor;
MVC girişindeki `email` scope'u tam olarak bunun için. O olmadan access token'ın içinde hiç adres bulunmuyor
ve bir satın alma buraya gönderilecek yer olmadan ulaşıyor. Alan yine de nullable, çünkü bir hesapta adres
hiç olmayabilir; öyle bir durumda consumer bunu bilet id'siyle birlikte söylüyor — birinin elle göndermesi
gerekebilir.

Mail, `IEmailService` portunun arkasından MailKit ile SMTP üzerinden çıkıyor. Framework'teki `SmtpClient`
yıllardır obsolete ve dokümantasyon da buraya işaret ediyor.

| Ayar | |
|---|---|
| `Smtp:Host` / `Smtp:Port` | `smtp.gmail.com` ve 587 — açık başlayıp STARTTLS ile yükseltilen submission portu |
| `Smtp:SenderName` / `Smtp:SenderEmail` | mesajın kimden geldiğini söylediği şey |
| `Smtp:UserName` | Google hesabının kendisi |
| `Smtp:Password` | Bir Google **App Password**: on altı karakter, uygulama başına üretilir, tek başına iptal edilebilir. Google düz hesap parolasını SMTP üzerinden tamamen reddediyor, ki bu doğru bir ret. |

Her iki kimlik bilgisi de diğer her şey gibi Vault'tan geliyor. Ama mail buradaki tek **opsiyonel** şey:
kimlik bilgisi olmayan bir klon yine bilet satar ve gönderecek yeri olmadığını kaydeder — kimsenin henüz
istemediği bir özellik yüzünden açılmayı reddeden bir uygulama yerine. Para taşıyan sırlara tam tersi
davranılıyor.

Gövde inline stilli tablolardan oluşuyor, çünkü bir mail istemcisinde ayakta kalan şey bu. Outlook Word ile
render ediyor, Gmail `<style>` bloklarını söküp atıyor, ve flexbox hiçbir yerde güvenilir biçimde
desteklenmiyor — mailde işleyen kurallar, web'in yirmi yıl önce geride bıraktığı kurallar. Mesajdan gelen her
değer girişte HTML-encode ediliyor, böylece içinde `&` olan bir takım adı düzeni bozamıyor, daha kötüsünü
yapmak şöyle dursun.

> **Başarısız bir gönderim tekrar denenmiyor.** Hatayı yutmak, MassTransit'in mesajı tüketilmiş saymasına yol
> açıyor; yani RabbitMQ'nun yeniden teslimi ve error kuyruğu bu yol için kapalı: otuz saniye kapalı kalan bir
> mail sunucusu o onayı kalıcı olarak kaybettirir ve yalnızca log hatırlar. Başarısız gönderimden sonra
> yeniden fırlatmak tek satır ve ikisini de geri açar — kuyruğunu zehirleyebilecek bir mesaj pahasına.


### Sağlayıcı geri konuştuğunda

Bir çekim, cevap geldiğinde bitmiş olmuyor. Kart çekilip cevap kaybolabilir — checkout başarısız sanır,
Stripe başarılı bilir. Para haftalar sonra, buradan kimsenin göremediği bir panelden geri gidebilir. Ve kart
sahibi aylar sonra bize değil bankasına gidebilir. Bunların hiçbirinin arkasında bizim bir isteğimiz yok,
dolayısıyla webhook olmadan bu sistem için **hiç var olmuyorlar**.

```
Stripe ──► POST /api/v1/payments/webhook   anonim, ham gövde, imza kontrolü
              │
              ├── imza tutmuyor ──► 400, başka hiçbir şey olmaz
              │
              └── doğrulandı ──► inbox_messages satırı ──► 200 (milisaniyeler içinde)
                                    │
                                    │  InboxProcessor, 5 sn'de bir
                                    ▼
                               RabbitMQ ──► consumer'lar
```

Bu, API'deki izin arkasında olmayan tek uç — çünkü Stripe'ın burada hesabı yok ve sunacak hiçbir şeyi yok.
Güvenliği tamamen imza sağlıyor, o yüzden:

- **Gövde ham metin olarak okunuyor**, asla bir modele bağlanmıyor. İmza gönderilen byte'lar üzerinden
  hesaplanır; girişte onları yeniden şekillendiren her şey imzayı yok eder.
- **`EventUtility.ConstructEvent` HMAC'i signing secret ile yeniden hesaplıyor** ve tutmayan her şeyi
  reddediyor — beş dakikadan eski, gerçek bir olayın tekrar oynatılması dahil.
- **Doğrulanmış bir olay olmayan her şey aynı şekilde reddediliyor**, sadece `StripeException` değil.
  Eksik bir `Stripe-Signature` başlığı parser'a `NullReferenceException` fırlattırıyor; anonim bir uçta bu,
  gönderene teslim edilen bir 500 ve bir stack trace demek.
- **Signing secret yoksa hiçbir şey kabul edilmiyor.** Doğrulanamayan bir webhook, ödemenin başarılı
  olduğunu iddia eden bir yabancıdır; secret ayarlanmadı diye kabul etmek zafiyetin ta kendisidir.

Sürüm uyuşmazlıkları ise bilerek reddedilmiyor. Bir Stripe hesabının kendi API sürümü vardır, panelden
ayarlanır ve gayet makul olarak bu SDK'nın derlendiği sürüm değildir; bunun üzerinden reddetmek çalışan bir
entegrasyonu sessiz bir kesintiye çevirirdi.

### Inbox

Outbox'ın aynası, ve ayrı bir tablo olmasının bir sebebi var. Outbox satırı yerel bir transaction'ın sonucudur
ve onun kaderini paylaşır. Inbox satırı ise dışarıdan gelir, arkasında bize ait hiçbir transaction yoktur ve
outbox'ta olmayan bir şeye ihtiyaç duyar:

| Kolon | |
|---|---|
| `provider_event_id` | **unique** — Stripe bir olayı üç güne kadar tekrar gönderir |
| `provider_event_type` | `payment_intent.succeeded` ve arkadaşları, iz kalsın diye |
| `type` | bizim entegrasyon olayımız, tam tip adıyla |
| `payload` | çevrilmiş olay, JSON olarak |
| `received_on_utc`, `processed_on_utc`, `attempts`, `failed_on_utc`, `error` | outbox'takiyle birebir aynı |

O unique index, tekrar teslimi bir **no-op**'a çeviren şey: ikinci insert başarısız olur, endpoint ilk gelenin
aldığı 200'ün aynısını cevaplar, ve hiçbir bilet iki kez iptal edilmez. Mükerrer engelleme, her consumer'ın
sormayı hatırlaması gereken bir şey olmaktan çıkıp veritabanının çözdüğü bir gerçek haline gelir.

Stripe'ın şekli **kenarda**, Stripe SDK'sının yaşadığı yerde çevriliyor; böylece endpoint'in aşağısındaki
hiçbir şey Stripe'ı hiç duymuyor — sweeper, outbox sweeper'ının okuduğunun aynısını okuyor. Stripe pek çok
olay türü gönderiyor, bu sistem üçünü kullanıyor; geri kalanı doğrulanıp onaylanıyor ve düşürülüyor — hiçbir
şeyin okumadığı satırlarla bir tabloyu doldurmak yerine.

### Üç olay ne yapıyor

| Olay | |
|---|---|
| `payment_intent.succeeded` | **Mutabakat.** Bu çekime ait bir bilet var mı? Neredeyse her zaman var ve hiçbir şey olmuyor. Olmadığında ise biri, sistemin sattığını düşünmediği bir koltuk için ödeme yapmıştır; bu, düzeltmek için gereken metadata ile birlikte `Error` seviyesinde loglanır. |
| `charge.dispute.created` | **Ters ibraz.** Bilet iptal edilir, koltuk yeniden satışa çıkar, sayaçlar düzeltilir. |
| `charge.refunded` | Ya biri panelden iade tuşuna basmıştır — bileti iptal et — ya da bu uygulamanın kendi telafisi geri yankılanmaktadır; orada bilet yoktur çünkü satış geri alınmıştı. Aynı handler ikisini de canlı bilete bakarak ve yoksa hiçbir şey yapmayarak cevaplar. |

Bir itiraz henüz bir kayıp değil, bir iddiadır: fonlar bloke edilir ve kazanılabilir. Bilet yine de iptal
edilir, çünkü koltuk belirli bir akşama ait fiziksel bir şeydir ve ters ibraz ettiği bir koltukta birinin
oturmasına izin vermek daha büyük hatadır. Kazanmak bileti kendiliğinden geri getirmez — o, bir insanın
karar vermesini ister.

Bütün bunları mümkün kılan şey **korelasyon**. Bir webhook bir çekimi bilir, başka hiçbir şeyi bilmez; bu
yüzden `PaymentIntent` maçı, koltuğu ve alıcıyı metadata'sında taşıyarak oluşturuluyor, bilet de kendisini
ödeyen çekimi kaydediyor. İkisi olmadan haftalar sonra gelen bir olay, kimsenin üzerine iş yapamayacağı bir
numaradan ibarettir.

### Yerelde test etmek

```powershell
winget install --id Stripe.StripeCli --exact
stripe login
stripe listen --forward-to localhost:5042/api/v1/payments/webhook
```

`stripe listen` bir signing secret yazdırır — `PaymentProvider:WebhookSecret` odur. **Her başlatıldığında
yenisini yazdırır**; sabit bir secret, Stripe panelinden tanımlanan bir endpoint'ten gelir. Sonra başka bir
terminalde:

```powershell
stripe trigger payment_intent.succeeded
stripe trigger charge.dispute.created
stripe trigger charge.refunded
```

## Arka plan işleri

İki worker, ikisi de `PeriodicTimer` üzerinde düz `BackgroundService`. Hangfire yok, Quartz yok: iki periyodik
işin kendine ait bir scheduler'a, bir dashboard'a ve bir tablo setine ihtiyacı yok — ve bir scheduler'ın asıl
işe yarayacak özelliği, *bu iş yalnızca tek bir instance'ta çalışsın*, önemli olan yerde zaten
`FOR UPDATE SKIP LOCKED` ile çözülmüş durumda.

| Worker | Sıklık | Ne yapıyor |
|---|---|---|
| `ExpiredReservationCleanupWorker` | dakikada bir | tutması dolan koltukları geri veriyor |
| `OutboxProcessor` | 5 saniyede bir | yayınlıyor, derinliği sayıyor, günde bir eski satırları siliyor |

### Terk edilmiş koltukları geri vermek

Bir tutma on dakika sürüyor, ve uzun süre onu bitiren tek şey **başka birinin aynı koltuğu almaya
çalışması**ydı — aggregate, geçerken süresi dolmuş tutmayı serbest bırakıyor. Bu, insanların kapıştığı bir
koltuk için işe yarar, geri kalanı için hiç yaramaz. Yarıda bırakılan bir checkout, koltuğu kalıcı olarak
`Reserved` bırakıyordu: maça karşı sayılıyor, tıklamayı akıl etmeyen hiç kimseye satılamıyor ve görünmüyordu.
Liste bir şey, koltuk haritası başka bir şey söylüyordu; ve bir maçın satılabilir koltuğu bitebiliyordu ama
`SoldOut`'a hiç ulaşmıyordu.

Serbest bırakmayı hâlâ domain yapıyor, koltuk koltuk, `Match.ReleaseSeat` üzerinden — böylece kurallar ve
event'ler ait oldukları yerde kalıyor. Yalnızca sayaçlar veritabanına devrediliyor, bir satışın onları
devretmesiyle aynı sebeple. Maç satırı ilk alınıyor, tıpkı satışın aldığı gibi, ki ikisi maça ve bir koltuğa
asla ters sırayla uzanmasın.

Okuma ile yazma arasında birinin satın aldığı ya da yeniden rezerve ettiği bir koltuk, concurrency kontrolüne
takılıp tüm maçı geri alıyor — ki bu bir hata değil, **doğru cevap**: koltuk yine kullanımda ve serbest
bırakılacak bir şey kalmamış. Bir sonraki tur hâlâ süresi dolmuş olanları alır.

### Sonsuza kadar denememek

Asla teslim edilemeyecek bir outbox mesajı — tanınmayan bir tip, deserialize olmayan bir payload — eskiden
süreç yaşadığı sürece her beş saniyede tekrar deneniyor, her seferinde bir log satırı yazıyor ve her partide
bir slot işgal ediyordu. `attempts` reddedilmeleri sayıyor, `failed_on_utc` ise sweeper'ın beş denemeden
sonra vazgeçtiklerini işaretliyor. O kolonu hiçbir şey kendiliğinden temizlemiyor: içinde saat olan bir satır
bir insanı bekliyor — aşağıdaki `dead` gauge'ı tam olarak bunun için var.

### Eskileri temizlemek

Teslim edilmiş bir mesaj işini yapmıştır ama satırı ondan uzun yaşar. Günde bir kez, otuz günden önce teslim
edilmiş her şey siliniyor — hakkında soru sorulabilecek kadar uzun, sweeper'ın her beş saniyede okuduğu
tablonun asla bakmayacağı sayfalara dönüşmeyeceği kadar kısa.

### Çalıştığını bilmek

```
stadiapass_outbox_pending   yazılmış ama broker'ın henüz almadığı mesajlar
stadiapass_outbox_dead      sweeper'ın vazgeçtikleri — sıfırın üstündeki her şey bir insan istiyor
stadiapass_inbox_pending    kaydedilmiş ama henüz bus'a konmamış sağlayıcı olayları
stadiapass_inbox_dead       sweeper'ın vazgeçtiği sağlayıcı olayları — sağlayıcı bunları bir daha göndermez
```

`pending`, tüm mesajlaşma yolu hakkındaki **tek en yararlı sayı**. Çöken bir broker, bozulan bir consumer ve
duran bir sweeper dışarıdan aynı görünür: tırmanan ve geri inmeyen bir sayı. O olmadan tek kanıt, kimsenin
okumadığı bir log satırı.

**`inbox_dead`, `outbox_dead`'den daha önemlidir ve bu farkı net söylemeye değer.** Bu sistemin gönderemediği
bir mesaj hâlâ kendi göndereceği mesajdır. **Sağlayıcının** gönderdiği bir mesaj ise `200` cevapladığımız
mesajdır: Stripe'a elimizde olduğu söylenmiştir ve onu bir daha asla göndermez. Dolayısıyla kenara ayrılmış
bir inbox satırı, kimsenin uygulamadığı bir ters ibraz ya da kimsenin mutabakatını yapmadığı bir ödemedir; o
satırın kendisi, olayın yaşandığına dair kalan son kanıttır. `> 0` için alarm kurulmaya değen gauge budur.

Gauge'lar veritabanını sorgulamak yerine sweeper'ın cache'lediği bir sayıyı okuyor. Observable gauge'ın
callback'i senkrondur ve collector'ın thread'inde çalışır; oraya konulan bir sorgu, PostgreSQL ne kadar
sürerse metrik toplamayı o kadar bloke ederdi. Sweeper zaten her beş saniyede o tablonun başında.

Meter, ServiceDefaults'ta kayıtlı; yani değerler diğer her şeyle aynı `/metrics` endpoint'inden çıkıyor ve
Prometheus onu zaten topluyor — bkz. [Metrikler ve dashboard'lar](#metrikler-ve-dashboardlar).

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
  oldu; initializer artık o günden beri yapılan şema değişiklikleri için kısa bir betik taşıyor:
  `CREATE TABLE IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS` ve `CREATE UNIQUE INDEX IF NOT EXISTS`. Bu
  geçici bir çözüm ve öyle de duruyor; onu ortadan kaldıracak şey migration'lardır.
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




