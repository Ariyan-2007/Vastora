# Vastora Blueprint

**This file is the single source of truth for the Vastora backend/API.** It records the full
vision, the architecture decisions, what has been built, and what's left — broken into
sessions small enough to execute without losing coherence. Read this file first in any
new session before writing code. Update the **Progress Log** and **Roadmap** checkboxes
at the end of every session, however small.

**Frontend implementation is out of scope for this file.** Three self-contained companion
blueprints cover it: [`docs/SUPEROFFICE_FRONTEND_BLUEPRINT.md`](docs/SUPEROFFICE_FRONTEND_BLUEPRINT.md),
[`docs/BACKOFFICE_FRONTEND_BLUEPRINT.md`](docs/BACKOFFICE_FRONTEND_BLUEPRINT.md), and
[`docs/ANTIVALY_SHOP_BLUEPRINT.md`](docs/ANTIVALY_SHOP_BLUEPRINT.md) — each has its own tech
stack, API contracts, screen breakdown, and (for BackOffice/SuperOffice) deployment config
spec, and is safe to move into that project's own repo. The one thing that *does* belong here:
if a session changes the API surface (new endpoint, DTO field renamed, enum value added),
update the relevant frontend doc(s) in the same session — they drift out of sync with the code
otherwise, same as this file.

---

## 1. The Idea

Vastora is a **universal, multi-tenant e-commerce API platform** — infrastructure that gets
sold as a subscription to other businesses, not a single storefront. One codebase, one
deployment, serving many unrelated sellers at once, each with their own branding, catalog,
staff, and customers, fully isolated from one another.

It is the spiritual successor to **Antivaly**, a student ASP.NET Web API / Entity Framework /
SQL Server e-commerce project (`Antivaly-main.zip`, studied at the start of this project — see
§8 for what was carried over). Antivaly had one hard-coded shop with Admin/Seller/Buyer/
DeliveryMan roles. Vastora generalizes that into a SaaS platform with three tiers of
subscriber instead of one fixed business.

### The business model, concretely

Vastora (the platform, operated by its owner) sells subscriptions to **Tenants** — the paying
customers. A Tenant is provisioned in one of two shapes:

1. **Single-business subscription.** A Tenant runs exactly one storefront. They get:
   - **Landing Page** — public marketing page for that business.
   - **Shop** — the public storefront customers browse and buy from.
   - **BackOffice** — the admin console for that one business (staff, catalog, orders).

2. **Multi-business subscription.** A Tenant is a group/holding company selling different
   product lines under different brand names, all owned by the same person. Each brand is
   its own **Business** with its own Landing Page + Shop + BackOffice, exactly as above,
   **plus**:
   - **SuperOffice** — a control panel that spans every Business the Tenant owns. Only
     Tenants of this shape get one.

So the object model has three tiers, not two:

```
Platform (Vastora itself)
  └─ Tenant (the paying subscriber — a business or a group of businesses)
       └─ Business (one storefront: Landing Page + Shop + BackOffice)
```

A `TenantAccount.Type` of `SingleBusiness` or `MultiBusiness` decides which shape a given
Tenant is — see §3 for how this is enforced (or deliberately left un-enforced) in code today.

### Roles, top to bottom

| Role | Scope | Console |
|---|---|---|
| `PlatformSuperAdmin` | Every Tenant on Vastora | Platform console (not yet built as UI; API exists) |
| `TenantOwner` | Every Business under their own Tenant | SuperOffice |
| `BusinessAdmin` | One Business | BackOffice (full) |
| `BusinessStaff` | One Business | BackOffice (limited — not yet differentiated, see §7) |
| `DeliveryAgent` | One Business, orders assigned to them | BackOffice (delivery queue) |
| `Customer` | One Business | Shop |

No frontend exists yet anywhere in this repo. This phase is API-only, deliberately — see §2.

---

## 2. What's in scope right now vs. later

The instruction driving this repo's first session was: **build the foundation and some
genuinely workable APIs**, not the whole mega-project in one sitting. Frontend work is
explicitly deferred until the API surface is solid. Everything in this document beyond
§7 ("What Exists Today") is **planned, not built** — treat it as a map, not a status report.

---

## 3. Architecture

Clean Architecture, four projects, dependencies point inward only:

```
Vastora.slnx
src/
  Vastora.Domain          — Entities, enums, no external dependencies (except MongoDB.Bson
                             for [BsonId] on the shared BaseEntity — a pragmatic exception
                             since MongoDB is a fixed, non-negotiable requirement here).
  Vastora.Application     — Use-case services, DTOs, validators, interfaces (repositories,
                             current-user context, password hasher, JWT issuer). Depends on
                             Domain only.
  Vastora.Infrastructure  — MongoDB repository implementation, JWT/BCrypt implementations,
                             startup DB initializer. Depends on Application + Domain.
  Vastora.API             — ASP.NET Core Web API: controllers, middleware, Program.cs.
                             Depends on all three.
tests/                    — Vastora.Application.Tests: xUnit unit tests for the Application
                             layer (§9.12). No integration test project yet — needs Docker.
docs/                     — placeholder for future design notes.
```

### Mapping from the legacy Antivaly project

Antivaly was a classic 4-project N-tier solution. Vastora's layers are its direct, modernized
descendants:

| Antivaly | Vastora | Notes |
|---|---|---|
| `BEL` (POCO models) | `Vastora.Domain` | Entities now carry Mongo `[BsonId]`, richer value objects (Address, embedded OrderItem/CartItem), and multi-tenant scoping via `ITenantScoped`/`IBusinessScoped`. |
| `BLL` (static service classes + AutoMapper) | `Vastora.Application` | Services are instance classes behind interfaces, DI-injected, no AutoMapper — mapping is done by hand in each service's `Map()` method (small enough not to need a mapping library yet). |
| `DAL` (EF6 + SQL Server, repository-per-entity) | `Vastora.Infrastructure` | One generic `MongoRepository<T>` instead of a hand-written repo class per entity — MongoDB's schema-less model makes the boilerplate unnecessary. |
| `AntivalyWebApi` (ASP.NET Web API 2, `System.Web.Http`) | `Vastora.API` | ASP.NET Core 10 Minimal Hosting + Controllers, JWT bearer auth instead of a custom DB-token `AuthorizationFilterAttribute`. |

### Multi-tenancy strategy

Shared database, shared collections, discriminator fields — **not** database-per-tenant.
Every tenant-owned document carries `TenantId` (via `ITenantScoped`) and most also carry
`BusinessId` (via `IBusinessScoped`). Isolation is enforced in the **Application layer**:
every service method takes the caller's `tenantId`/`businessId` (resolved from their JWT via
`ICurrentUserContext`, never trusted from the URL for self-scoped roles) and filters or
verifies against it before returning or mutating anything.

Every BackOffice-shaped controller carries `[Authorize(Policy = "BusinessMember")]`, backed by
`BusinessAccessAuthorizationHandler` (`Vastora.API/Authorization/`) — a real ASP.NET Core
`AuthorizationHandler<BusinessMemberRequirement>` that runs during `UseAuthorization()`, before
the action executes. It reads the request's `{businessId}` route value and decides:

- `BusinessAdmin` / `BusinessStaff` / `DeliveryAgent` → only their own `BusinessId` (from JWT).
- `TenantOwner` → any Business that belongs to their own `TenantId` (SuperOffice).
- `PlatformSuperAdmin` → any Business at all.

A denied check for the first group simply never calls `context.Succeed()`, which the framework
turns into a 403. A `TenantOwner`/`PlatformSuperAdmin` pointing at a Business that doesn't
exist or isn't theirs lets `NotFoundException` propagate out of the handler and through
`ExceptionHandlingMiddleware` as a 404 — deliberately indistinguishable from "doesn't exist."
On success, the handler stashes the Business's real `TenantId` on `HttpContext.Items` (via
`HttpContextTenantExtensions`), which controllers read back through the synchronous
`VastoraControllerBase.ResolvedTenantId` property — no second lookup, no imperative call at
the top of every action. (This replaced an earlier version that did the same check manually
inside each action; see Progress Log, 2026-08-13 §9.2 entry.)

### Why MongoDB

Given as a hard requirement, not a choice made here. `MongoDB.Driver` (official C# driver,
v3.x) is used directly through a thin generic repository (`IMongoRepository<T>` /
`MongoRepository<T>`) — no ORM layer on top, since Mongo's document model doesn't need one at
this scale. String `Id` fields map to Mongo `ObjectId` via `[BsonRepresentation(BsonType.ObjectId)]`.

---

## 4. Tech stack

- **.NET 10** / C# 13, ASP.NET Core Web API (Controllers, not Minimal APIs — the API surface
  is large enough that controller grouping and attribute routing pay for themselves).
- **MongoDB.Driver 3.x** — official driver, no ORM.
- **JWT bearer auth** (`Microsoft.AspNetCore.Authentication.JwtBearer`) — access + refresh
  tokens, refresh tokens hashed (SHA-256) and stored in Mongo, rotated on every refresh.
- **BCrypt.Net-Next** — password hashing (work factor 12).
- **FluentValidation** — request DTO validation. Every action argument is validated
  automatically by a global `ValidationActionFilter` (`Vastora.API/Filters/`) before the action
  body runs: it resolves `IValidator<T>` for the argument's type from DI (if one is registered)
  and throws `FluentValidation.ValidationException` on failure, which
  `ExceptionHandlingMiddleware` maps to a 400 with a per-field error dictionary. See Roadmap
  §9.1 (done 2026-08-15).
- **Swashbuckle (Swagger/OpenAPI)** — interactive API docs at `/swagger`, grouped into tag
  sections (Auth, Tenant Onboarding, Platform, SuperOffice, BackOffice - *, Shop - *) so a
  large endpoint surface stays navigable. XML doc comments on controllers are picked up
  automatically (`GenerateDocumentationFile` in `Vastora.API.csproj`).
- **DotNetEnv** — loads a root-level `.env` into configuration at startup (see §6).
- **Enums serialize as strings, not numbers** — `JsonStringEnumConverter` is registered
  globally (`Program.cs`), both directions. A `Product.Status` reads/writes as `"Active"`,
  never `2`. This applies everywhere, including the handful of endpoints whose request body
  *is* a bare enum (e.g. `PATCH /api/businesses/{id}/products/{id}/status` takes the JSON
  string `"Active"` directly as its body, not an object wrapping it).
- **Serilog** (`Serilog.AspNetCore`) — structured console logging + request logging
  (`UseSerilogRequestLogging()`); console sink only, no external aggregator configured yet
  (§9.11).
- **ASP.NET Core rate limiting** (`Microsoft.AspNetCore.RateLimiting`, built into the shared
  framework, no extra package) — a generous global fixed-window limit (100 req/min per IP) as
  abuse protection, distinct from `SubscriptionPlanLimits` (§9.9), which governs business-tier
  resource caps, not request rate (§9.11).
- **`Microsoft.Extensions.Diagnostics.HealthChecks`** — `GET /health` backed by a real MongoDB
  ping (`MongoHealthCheck`), not just a liveness stub (§9.11).
- **xUnit + Moq** (`tests/Vastora.Application.Tests`) — unit tests for the Application layer,
  using a hand-written `FakeMongoRepository<T>` (compiles and evaluates the real LINQ predicate
  against an in-memory list) rather than mocking `IMongoRepository<T>` directly, since a Moq
  stub can't exercise real filtering logic (§9.12).

---

## 5. Domain model

All entities live in `Vastora.Domain.Entities`, inherit `BaseEntity` (`Id`, `CreatedAt`,
`UpdatedAt`), and implement `ITenantScoped`/`IBusinessScoped` where relevant.

| Entity | Scoped to | Purpose |
|---|---|---|
| `TenantAccount` | — (root) | The subscriber. `Type` (Single/MultiBusiness, changeable via §9.4's `PATCH .../type`), `Status`, `Plan`, `OwnerUserId`. |
| `Business` | Tenant | One storefront. Slug (public, globally unique), branding fields, currency, status, `DeliveryModuleEnabled` (default `true` — §9.14), `DefaultDeliveryFee` (§9.7). |
| `AppUser` | Tenant (+Business except for PlatformSuperAdmin/TenantOwner) | Single table for every role, discriminated by `Role`. Embedded `Addresses` list. |
| `RefreshToken` | User | Hashed refresh tokens, rotation-friendly (`RevokedAt`, `ReplacedByTokenId`). |
| `PasswordResetToken` | User | Hashed, expiring (1hr), single-use (`UsedAt`) — same pattern as `RefreshToken` (§9.10). |
| `Category` | Business | Supports `ParentCategoryId` for subcategories — same collection, no join; `GET .../categories/tree` nests it (§9.5). |
| `Product` | Business | Price, `CompareAtPrice`, `DiscountPercent`/`DiscountExpiresAt` → `EffectivePrice` computed property. `StockQuantity`/`TrackInventory` (mutated only via `IInventoryService`, never directly — §9.15). `ReorderThreshold`/`ReorderQuantity` (§9.15b). Embedded `Variants` list (catalog-only, no Cart/Order integration — §9.5). |
| `Coupon` | Business | Percentage or fixed discount, usage cap, validity window. `IsValidNow` computed property. |
| `Cart` | Business + Customer | One live cart per customer per business, embedded `CartItem` list, optional coupon code. |
| `Order` | Business | Embedded `OrderItem` snapshot (price/name captured at checkout, immune to later product edits), `StatusHistory` + `PaymentStatusHistory` audit trails, `PaymentStatus` separate from fulfillment `Status` (transition rules enforced — §9.7). |
| `DeliveryAgentProfile` | Business | Operational stats for a `DeliveryAgent` user — status, balance (credited on delivery — §9.7), level, completed count. |
| `StockMovement` | Business | One row per `Product.StockQuantity` change — Sale/Restock/Return/Adjustment/DamageWriteOff, signed `QuantityDelta` (§9.15a). |
| `LedgerEntry` | Business | Revenue/Refund/DeliveryPayout, written only by `OrderService` on Delivered/Refunded transitions and agent payout (§9.16a) — no direct-write endpoint. |
| `Expense` | Business | Manually entered cost not tied to an Order (rent, ads, wages) — full BackOffice CRUD (§9.16b). |

---

## 6. Configuration & running locally

### Environment variables (`.env`, gitignored — copy from `.env.example`)

```
MONGODB_URI=<full mongodb+srv:// or mongodb:// connection string>
MongoDb__DatabaseName=vastora
Jwt__Secret=<long random string>
Jwt__Issuer=Vastora
Jwt__Audience=VastoraClients
Jwt__AccessTokenMinutes=30
PlatformAdmin__Email=<your email>
PlatformAdmin__Password=<blank to auto-generate on first run>
```

`Program.cs` loads `.env` via `DotNetEnv.Env.TraversePath().Load()` (searches upward from the
working directory, so it's found from the repo root or from inside `src/Vastora.API`) before
`WebApplication.CreateBuilder` runs. `MONGODB_URI` / `MONGODB_CONNECTION_STRING` are accepted
as friendly aliases for the stricter `MongoDb__ConnectionString` double-underscore form.

The **Mongo connection string currently in `.env` is a live Atlas cluster** the project owner
provided at kickoff (`cluster0.lgfe8k2.mongodb.net`, database name `vastora`). It was used for
this session's smoke test (see §7) — a couple of test tenants/businesses/orders exist in that
database as a result. Safe to wipe the `vastora` database before real use.

### First run

```bash
dotnet run --project src/Vastora.API/Vastora.API.csproj
```

On first run against an empty database, `DatabaseInitializer`:
1. Creates unique indexes (`TenantAccount.Slug`, `Business.Slug`, `Category`/`Product`
   `(BusinessId, Slug)`, `Coupon (BusinessId, Code)`) and an index on `RefreshToken.TokenHash`.
2. Seeds exactly one `PlatformSuperAdmin` account if none exists, using
   `PlatformAdmin:Email`/`PlatformAdmin:Password` from config — **if the password is left
   blank, a random one is generated and printed once to the startup log** (`warn` level).
   Capture it then; it is not stored anywhere else in plaintext.

Swagger UI: `http://localhost:<port>/swagger`.

---

## 7. What exists today (foundation session — 2026-08-13)

Everything below was built, compiled, and **smoke-tested against the live Atlas database**
in this session. Not a paper design — every endpoint listed here was actually called with
`curl` and returned the expected result before being marked done.

### Verified end-to-end in this session
- Platform admin auto-seeded on first run, logs in, `GET /api/auth/me` works.
- `POST /api/tenants/signup` provisions Tenant + Owner user + first Business + a working JWT
  session in one call.
- BackOffice: created a Category, created a Product, activated it.
- Public Shop: storefront info, category list, product catalog all resolve by business slug
  with no auth.
- Customer self-registration on a specific shop, add-to-cart, checkout — verified the order
  was created, **stock was decremented** (25 → 23 after ordering 2), and the order appears in
  the BackOffice order list.
- Tenant isolation: a second tenant owner's JWT gets **404** (not 403 — deliberately
  indistinguishable from "doesn't exist") when hitting the first tenant's business endpoints.
- Unauthenticated request to a protected endpoint → **401**.

### Full endpoint inventory

**Auth & onboarding**
- `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout` — BackOffice/SuperOffice/Platform realm (any role except Customer).
- `GET /api/auth/me`, `PUT /api/auth/me` — any authenticated role.
- `POST /api/auth/forgot-password`, `POST /api/auth/reset-password` — public; the latter shared by every realm including Shop (§9.10).
- `POST /api/tenants/signup` — public; provisions Tenant + Owner + first Business.
- `GET /api/tenants/me` — TenantOwner only.
- `GET /api/tenants/me/usage` — TenantOwner only; usage vs. plan limits (§9.9).
- `POST /api/shop/{businessSlug}/auth/register`, `POST /api/shop/{businessSlug}/auth/login`, `POST /api/shop/{businessSlug}/auth/forgot-password` — Customer realm, scoped to one Business by slug.

**Platform (`PlatformSuperAdmin` only)**
- `GET /api/platform/tenants`, `GET /api/platform/tenants/{tenantId}`
- `PATCH /api/platform/tenants/{tenantId}/status`, `PATCH /api/platform/tenants/{tenantId}/plan`, `PATCH /api/platform/tenants/{tenantId}/type` — the last is Single↔MultiBusiness (§9.4).
- `GET /api/platform/tenants/{tenantId}/usage` — any Tenant's usage vs. plan limits (§9.9).

**SuperOffice (`TenantOwner` only, spans every Business they own)**
- `GET/POST /api/superoffice/businesses`
- `GET/PUT /api/superoffice/businesses/{businessId}`, `PATCH .../status`
- `GET /api/superoffice/analytics` — cross-business revenue/orders/top-products rollup (§9.8).

**BackOffice (`BusinessAdmin`/`BusinessStaff`, + `TenantOwner`/`PlatformSuperAdmin` via the `BusinessMember` policy)**
- `GET/PUT /api/businesses/{businessId}` — self profile (Staff excluded from `PUT`).
- `PATCH /api/businesses/{businessId}/delivery-module` — toggle `DeliveryModuleEnabled` (Admin/TenantOwner/Platform, not Staff; §9.14).
- `GET/POST /api/businesses/{businessId}/staff`, `PATCH .../staff/{userId}/status`, `GET .../customers`.
- `.../categories`: full CRUD, `DELETE` Admin-tier only (§9.3); `GET .../categories/tree` (§9.5).
- `.../products`: full CRUD, `DELETE` Admin-tier only (§9.3); `PATCH .../status`; `POST .../{productId}/images` (multipart, §9.5).
- `.../coupons`: `GET` any BackOffice role, `POST`/`PUT`/`DELETE` Admin-tier only (§9.3).
- `GET .../products/{productId}/stock-movements`, `POST .../products/{productId}/stock-adjustments`, `GET .../inventory/low-stock`, `GET .../inventory/valuation` — §9.15.
- `GET/POST .../expenses`, `PUT/DELETE .../expenses/{expenseId}`, `GET .../accounting/profit-and-loss`, `GET .../accounting/balance-sheet` — Admin-tier only, not Staff (§9.16).
- `GET .../delivery-agents`, `GET/PATCH .../delivery-agents/me`, `PATCH .../delivery-agents/{userId}/status`.
- `GET .../orders`, `GET .../orders/{orderId}`, `PATCH .../orders/{orderId}/status` (transitions validated — §9.7), `PATCH .../orders/{orderId}/payment-status` (§9.6), `PATCH .../orders/{orderId}/assign-delivery`, `GET .../orders/assigned-to-me` (DeliveryAgent).

**Shop (public + `Customer` role)**
- `GET /api/shop/{businessSlug}`, `.../categories`, `.../products` (search pushed server-side — §9.5), `.../products/{productId}` — all public, no auth.
- `GET/POST/PUT/DELETE /api/shop/cart`, `POST .../cart/items`, `PUT/DELETE .../cart/items/{productId}`, `POST .../cart/coupon` — Customer only, scope from JWT.
- `POST /api/shop/orders/checkout`, `GET /api/shop/orders`, `GET .../{orderId}`, `POST .../{orderId}/cancel` — Customer only.

**Static files**
- `GET /uploads/{businessId}/{fileName}` — public; serves whatever `POST .../products/{id}/images` wrote (§9.5, local disk today).

**Health**
- `GET /health` — public; a real MongoDB ping, not just "the process is up" (§9.11).

### Deliberate simplifications — updated 2026-08-15

Most of the foundation-session list below was resolved in the session that added §9.3–9.8,
§9.10–9.12, §9.15–§9.16 (see the Progress Log). What's left is what's genuinely still cut:

- **No payment gateway.** Checkout still creates an order with `PaymentStatus.Pending`
  immediately (cash-on-delivery-style) — this is unchanged. What *did* change: staff can now
  manually record a payment via `PATCH .../orders/{id}/payment-status` (§9.6), so COD
  businesses have a real way to mark an order paid. A real gateway (Stripe/SSLCommerz/bKash)
  still needs an explicit choice — that's a product decision, not something to fabricate
  credentials for.
- **No self-serve Single→MultiBusiness upgrade request flow.** The technical piece
  (`PATCH /api/platform/tenants/{id}/type`, §9.4) is done and Platform-driven; a TenantOwner
  still can't request one themselves. Still needs a product decision on the desired process.
- **Product variants are catalog-only.** §9.5 added `Product.Variants` (embedded,
  BackOffice-manageable) but Cart/Order still reference a bare `ProductId` — selecting a
  specific variant at checkout isn't wired up. See §9.5 in the Progress Log for the exact
  boundary.
- **Local disk file storage, not cloud.** §9.5's image upload writes to local disk
  (`LocalFileStorageService`) behind an `IFileStorageService` interface specifically so
  swapping to S3/Azure Blob/Cloudinary is a new implementation, not a rewrite. Won't survive a
  redeploy on most PaaS hosts and doesn't scale past one instance — fine for now, not for
  production.
- **No real email/SMS delivery.** §9.10 added `INotificationService`, but the only
  implementation (`LoggingNotificationService`) just logs — including password-reset tokens,
  which is how the session's own smoke test retrieved one to complete the reset flow. A real
  provider (SendGrid/SES/Twilio/...) needs to be chosen and wired in behind the same interface.
- **Integration tests still don't exist.** §9.12 added a real unit test suite (`tests/`, 37
  tests as of this session, `dotnet test` from the repo root) covering the business-rule-heavy
  services, but Testcontainers-hosted MongoDB integration tests are still blocked — this
  session's environment had no Docker available to build or verify them against.
- **CORS defaults to wide open, but is now configurable.** `Cors:AllowedOrigins` in config
  (§9.11) restricts it once set; with nothing configured it still falls back to
  `AllowAnyOrigin` for local Swagger/Postman use — set it before real client traffic.
- **Single-entry, cash-basis accounting, not double-entry/GAAP.** §9.16's `LedgerEntry`/P&L/
  balance-sheet are a deliberately simple model — a real accountant would want more than this
  before treating it as a system of record.

---

## 8. What was learned from Antivaly (for context on domain shape)

Antivaly (studied via `Antivaly-main.zip`) was a single-shop ASP.NET Web API 2 + EF6 project:
`BEL`/`BLL`/`DAL`/`AntivalyWebApi` layers, SQL Server via an EDMX model, plain-text password
comparison in `AuthRepo.Authenticate` (fixed in Vastora — BCrypt from day one), GUID bearer
tokens stored directly in a `Tokens` table with no expiry/rotation (fixed — Vastora refresh
tokens are hashed, expiring, and rotated), and a `UserID` sequence table used to hand-roll
per-role incrementing IDs (`AccType` → `LastID`) instead of relying on the database's own key
generation — not carried over; Mongo `ObjectId`s are used directly. Its order model
(`TransactionRepo.PlaceOrder`) serialized the cart to a JSON blob into a `TDetials` string
column via `JavaScriptSerializer` rather than a normalized items table — replaced in Vastora
by a proper embedded `OrderItem` list on the `Order` document, which is Mongo-idiomatic and
gives per-item structure without needing a join.

The `Antivaly-main.zip` file remains in the repo root for reference; it is git-ignored (see
`.gitignore`) since it's a large binary snapshot, not source the team edits.

---

## 9. Roadmap — sessions, not sprints

Each numbered item below is meant to be small enough to complete, verify, and document in one
sitting without losing coherence, the way §7 was completed in this session. Check items off
as they land and add a dated entry to the Progress Log. Feel free to reorder based on what's
actually useful next — this is a map, not a contract.

As of 2026-08-15, §9.1–§9.3, §9.5, §9.7–§9.9, §9.11–§9.16 are fully done, and §9.4/§9.6/§9.10/
§9.12 are done except for the pieces each one explicitly flags as blocked on a product decision
(payment gateway choice, real email/SMS provider, self-serve tenant-upgrade process) or missing
infrastructure (Docker, for integration tests). What's left to pick up next is exactly those
flagged gaps — read each section's own "not done" note for the specifics rather than treating
this paragraph as the authority; it will go stale faster than they will.

### 9.1 Wire up request validation — done (2026-08-15)
- [x] Added `ValidationActionFilter` (`Vastora.API/Filters/`), a global `IAsyncActionFilter`
      registered via `options.Filters.Add<ValidationActionFilter>()` in `Program.cs`. It
      resolves `IValidator<T>` for each action argument's runtime type from DI and, if one is
      registered, runs it before the action body executes; failures across all arguments are
      collected and thrown as one `FluentValidation.ValidationException`, which
      `ExceptionHandlingMiddleware` already mapped to a 400 — no middleware changes needed.
      Smoke-tested against the live Atlas database: `POST /api/tenants/signup` with a
      malformed email + short password now 400s with FluentValidation's own messages (not
      `[ApiController]`'s built-in required-field check, which fires first and separately for
      missing fields), `POST .../categories` with an empty name 400s, and the golden path
      (valid tenant signup, valid category create) still succeeds unchanged.

### 9.2 Proper resource-based authorization — done (2026-08-13)
- [x] Replaced `EnsureBusinessAccessAsync` with a `BusinessMemberRequirement` +
      `BusinessAccessAuthorizationHandler` policy (`Vastora.API/Authorization/`), declarative
      on controllers via `[Authorize(Policy = "BusinessMember")]`. See §3 for the mechanism.
      Follow-up ideas, not blocking: a custom `AuthorizationPolicyProvider` if per-role policy
      variants ever need to be generated dynamically instead of the fixed set today; unit
      tests for the handler's three role branches (currently only smoke-tested via curl).

### 9.3 Real BusinessAdmin vs BusinessStaff permission split — done (2026-08-15)
- [x] Decided the split with hardcoded role checks (not a configurable per-business
      permissions list) — consistent with every other authorization mechanism in this codebase
      (`[Authorize(Roles = ...)]`), and there's no other configurable-per-business permission
      system anywhere to justify the extra complexity yet. Staff keeps day-to-day catalog/order
      work; Admin-tier (`BusinessAdmin`/`TenantOwner`/`PlatformSuperAdmin`) gets destructive or
      revenue-sensitive actions: `DELETE .../products/{id}`, `DELETE .../categories/{id}`, and
      all of `.../coupons` (`POST`/`PUT`/`DELETE` — Staff keeps read-only `GET`), on top of the
      pre-existing `PUT /api/businesses/{id}` restriction. Same split applied to the two new
      modules that landed in this session: `.../expenses` and `.../accounting/*` (§9.16) are
      Admin-tier only, since financial data is at least as sensitive as coupons.
      Smoke-tested: a `BusinessStaff` JWT gets 403 on product delete and coupon create, 200 on
      a product `PUT` (unaffected).

### 9.4 Tenant lifecycle completeness — partially done (2026-08-15)
- [x] `PATCH /api/platform/tenants/{id}/type` — `ITenantService.UpdateTypeAsync`. Upgrade
      (Single→MultiBusiness) always allowed; downgrade (MultiBusiness→Single) blocked with a
      `ConflictException` if the Tenant currently owns more than one Business. Not
      independently curled this session (no PlatformSuperAdmin credentials available, same gap
      noted in §9.9) — verified by code review and by the existing pattern it copies exactly
      (`UpdatePlanAsync`/`UpdateStatusAsync` on the same controller, both already proven).
- [ ] Self-serve upgrade request flow (TenantOwner requests, Platform approves?) — still not
      built. Explicitly a product decision, not something to invent unprompted.

### 9.5 Catalog depth — done (2026-08-15)
- [x] **Nested category trees.** `ICategoryService.GetTreeAsync` groups the existing flat
      `Category` collection by `ParentCategoryId` and nests recursively — no schema change, as
      predicted. `GET .../categories/tree` added alongside the existing flat `GET`. Smoke-tested
      live: created a parent + child category, `GET .../tree` returned the child correctly
      nested under the parent.
- [x] **Product variants.** New embedded `ProductVariant` (Id/AttributeSummary/Sku/
      PriceOverride/StockQuantity) on `Product.Variants`, managed via `CreateProductRequest`/
      `UpdateProductRequest`. **Deliberately catalog-only** — Cart/Order still reference a bare
      `ProductId`, no variant selection at checkout. Threading variants through Cart/Order would
      mean redesigning `CartItem`/`OrderItem`, which is a bigger lift than "add embedded
      variants to Product" — scoped down on purpose rather than half-building it. Smoke-tested:
      created a product with one variant, server-generated its `Id`.
- [x] **Image upload.** New `IFileStorageService` abstraction (`Vastora.Application/Common/
      Interfaces/`) with one implementation, `LocalFileStorageService` (Infrastructure) — writes
      to local disk under the app's base directory, served back via `StaticFileOptions` at
      `/uploads/{businessId}/{file}`. `POST .../products/{id}/images` (multipart, 5MB limit,
      JPEG/PNG/WEBP/GIF only, server-generated filename — never trusts the caller's raw
      filename) appends the resulting URL to `Product.Images`. Deliberately not S3/Cloudinary —
      the interface exists specifically so that's a swap-in later, not a rewrite; local disk
      won't survive a redeploy on most PaaS hosts. Smoke-tested end to end: uploaded a real PNG,
      confirmed the URL in the response, fetched it back over HTTP (200).
- [x] **Search pushed server-side.** `ProductService.GetPublicCatalogAsync` used to fetch every
      Active product for the Business into memory, then filter category/search with LINQ — now
      all filters are in the `FindAsync` predicate itself, evaluated by MongoDB, using
      `Regex.IsMatch(field, Regex.Escape(search), RegexOptions.IgnoreCase)` for case-insensitive
      substring matching (`Regex.Escape` guards against the search string being read as a regex
      pattern). Still not Atlas Search — no relevance ranking, no typo tolerance — but no longer
      "load everything, filter in C#" either. Smoke-tested: case-insensitive substring search
      returned the expected single match.

### 9.6 Payments — partially done (2026-08-15)
- [x] **Manual payment recording.** `PATCH .../orders/{id}/payment-status` — the missing piece
      for the cash-on-delivery flow that's the only one that exists: previously there was no way
      to ever mark an order `Paid` at all. Writes a `PaymentStatusEvent` audit entry alongside
      `Order.PaymentStatus`, mirroring the existing `StatusHistory` pattern. Smoke-tested live:
      marked a delivered order's payment `Paid`, 200.
- [ ] **Real gateway integration still not done, and deliberately not faked.** Needs a gateway
      chosen (Stripe vs. a Bangladesh-local option like SSLCommerz/bKash) — an explicit product
      decision. No credentials exist to build against, so nothing webhook-shaped was built this
      session; inventing a non-functional integration would be worse than leaving this open.

### 9.7 Delivery & fulfillment depth — done (2026-08-15)
- [x] **Delivery fee default.** New `Business.DefaultDeliveryFee` (decimal, settable via the
      existing `PUT /api/businesses/{id}`). `CheckoutRequest.DeliveryFee` is now `decimal?` —
      when the caller omits it, `OrderService.CheckoutAsync` falls back to the Business's
      default. Still no zone/distance calculation — that needs geodata this session doesn't
      have. Smoke-tested: set a Business's default fee to 12.50, checked out with no
      `deliveryFee` in the request, got `12.50` back on the order.
- [x] **Order status state machine.** `OrderService.AllowedTransitions` — a fixed
      `Dictionary<OrderStatus, OrderStatus[]>` (`PendingPayment`→Processing/Cancelled,
      `Processing`→Confirmed/OutForDelivery/Cancelled, `Confirmed`→OutForDelivery/Cancelled,
      `OutForDelivery`→Delivered/Cancelled, `Delivered`→Refunded, `Cancelled`/`Refunded`
      terminal). `Processing`→`OutForDelivery` is legal specifically because
      `AssignDeliveryAgentAsync` already made that exact jump — the table mirrors it rather than
      contradicting it. An illegal transition 409s with the specific from/to in the message; a
      same-status "transition" is a deliberate no-op, not an error (lets staff re-note a status
      without a spurious 409). This also gave §9.16a's ledger writes a structural guarantee for
      free: `Cancelled` is unreachable from `Delivered` and `Refunded` is only reachable from
      it, so refund bookkeeping never needs to check `PaymentStatus` — see §9.16a. Smoke-tested:
      `Processing`→`Delivered` (illegal) 409'd with the exact message; the full legal path
      (assign agent → `OutForDelivery` → `Delivered`) succeeded.
- [x] **Delivery agent earnings.** `OrderService.CreditDeliveryAgentAsync`, called on the
      `→ Delivered` transition: credits the assigned agent's `DeliveryAgentProfile.Balance` by
      their flat `DeliveryCharge` and increments `CompletedDeliveries`. No-op if no agent was
      assigned (e.g. the delivery module was off for that order). Smoke-tested: an agent's
      balance went from `0` to `20` (their `DeliveryCharge`) after their assigned order was
      marked `Delivered`, `CompletedDeliveries` went `0` → `1`.

### 9.8 SuperOffice depth — done (2026-08-15)
- [x] `GET /api/superoffice/analytics` (new `IAnalyticsService`) — revenue and order counts
      per Business plus a top-10-products-by-quantity list, all scoped to the caller's Tenant.
      "Revenue" is recognized on `Status == Delivered` only (never at order placement) —
      deliberately the same recognition rule §9.16a's sales ledger uses, so this dashboard
      number and the accounting module's number can never quietly disagree with each other.
      "Order count" is broader (every non-`Cancelled` order), since a still-`Processing` order
      is real pipeline activity worth seeing before it's revenue. Smoke-tested live: after one
      $412.48 delivered order, the endpoint returned that exact revenue figure, `orderCount: 1`,
      and the ordered product as the top product.

### 9.9 Subscription & billing — feature-gating and usage metering done (2026-08-15)
- [x] **Feature-gating by plan.** New `SubscriptionPlanLimits.For(SubscriptionPlan)`
      (`Vastora.Application/Tenants/SubscriptionPlanLimits.cs`) — static, in-code per-plan caps
      (`MaxBusinesses`/`MaxStaffPerBusiness`/`MaxProductsPerBusiness`, `null` = unlimited):
      Trial 1/3/20, Starter 1/10/200, Growth 5/50/2000, Enterprise unlimited. Enforced at the
      three places that actually consume a limit — `BusinessService.CreateAsync` (business
      count, layered on top of the pre-existing `TenantType.SingleBusiness` cap, not replacing
      it — a `MultiBusiness` Tenant is now *also* capped by its plan), `UserService.CreateStaffAsync`
      (staff count per Business, counting `BusinessAdmin`+`BusinessStaff`+`DeliveryAgent`
      together), and `ProductService.CreateAsync` (product count per Business) — each throws a
      `ConflictException` (409) naming the plan and the limit before creating anything.
- [x] **Usage metering.** `GET /api/tenants/me/usage` (TenantOwner) and
      `GET /api/platform/tenants/{tenantId}/usage` (PlatformSuperAdmin) both call the same new
      `ITenantService.GetUsageAsync`, returning `TenantUsageResponse` — the Tenant's plan,
      Business count vs. limit, and per-Business staff/product counts vs. limit. Deliberately
      `TenantOwner`-only on the tenant-facing route (matches the existing `GET /api/tenants/me`
      restriction) — `BusinessAdmin`/`BusinessStaff` can't call it, so BackOffice can only react
      to the 409s, not pre-check; flagged as a documented gap in the BackOffice blueprint rather
      than silently left unmentioned.
- [x] Smoke-tested against the live Atlas database with a fresh Trial-plan `MultiBusiness`
      tenant: created 3 staff (200 ×3), 4th 409'd with the exact plan message; created 20
      products (200 ×20), 21st 409'd; attempted a 2nd Business — 409'd through the *new* plan
      check specifically (confirmed by the message text, since this tenant's `MultiBusiness`
      type means the older type-based check didn't fire); `GET /api/tenants/me/usage` matched
      every count exactly (`businessCount: 1/1`, `staffCount: 3/3`, `productCount: 20/20`).
- **Not verified, and said so rather than guessed:** the Platform-only mirror endpoint
      (`GET /api/platform/tenants/{tenantId}/usage`) and the `PATCH .../plan` upgrade path
      itself weren't independently curled this session — this session had no PlatformSuperAdmin
      credentials on hand (the seeded admin's password was generated once, at kickoff, and
      never stored anywhere retrievable). Both share code with what *was* verified: the
      endpoint calls the identical `GetUsageAsync` already proven correct via the TenantOwner
      route, and `UpdatePlanAsync`/its controller action are pre-existing, unchanged code from
      the foundation session. Low risk, but noted here rather than claimed as tested.
- **Not done, out of scope for this session:** actual payment gateway / billing integration.
      This still needs a gateway chosen (Roadmap §9.6 — Stripe vs. a Bangladesh-local option —
      is an explicit product decision, not something to fabricate credentials for) before
      `PaymentStatus` can transition off `Pending` from a real charge. Plan limits and usage
      numbers exist now; *charging* for a plan does not.

### 9.10 Notifications — done (2026-08-15), no real provider
- [x] **Password reset flow** — the "there's no password-reset flow at all" gap called out
      when this item was originally written is closed. New `PasswordResetToken` (hashed,
      1-hour expiry, single-use — same shape as `RefreshToken`). `POST /api/auth/forgot-password`
      (BackOffice/Platform realm) and `POST /api/shop/{slug}/auth/forgot-password` (Shop realm)
      both always 204 whether or not the email matches an account — no user enumeration.
      `POST /api/auth/reset-password` is shared by every realm (the token itself identifies the
      account) and revokes every active `RefreshToken` for that user on success, forcing
      re-login everywhere — a reset is a "something may be compromised" signal. Smoke-tested the
      complete loop live: requested a reset, pulled the token out of the server log (see next
      bullet), reset the password, confirmed the pre-reset refresh token was rejected (401),
      logged in with the new password (200), and confirmed the same reset token couldn't be
      reused (401).
- [x] **`INotificationService`** (`Vastora.Application/Common/Interfaces/`) — one
      implementation, `LoggingNotificationService` (Infrastructure), which logs at Warning
      level instead of sending anything, since no email/SMS provider is configured. This is not
      a cosmetic stub: it's how the password-reset token above got retrieved for the smoke
      test, and it's genuinely usable for local dev/demo. Wired into `OrderService` for order
      confirmation (on checkout) and status-change notifications, both **best-effort** — wrapped
      in try/catch so a notification failure can never fail the order operation that triggered
      it (matters more once a real provider with real transient failures replaces the logger).
      A real provider (SendGrid/SES/Twilio/...) needs to be chosen and dropped in behind the
      same interface — deliberately not invented without one to build against.

### 9.11 Observability & hardening — done (2026-08-15)
- [x] **Structured logging.** `Serilog.AspNetCore`, console sink only (no external aggregator
      configured — Seq/ELK/Datadog would need one chosen first), plus `UseSerilogRequestLogging()`
      for per-request timing/status logs. Confirmed live in the smoke-test server's own log output.
- [x] **Rate limiting.** Built-in `Microsoft.AspNetCore.RateLimiting` (no extra package needed
      on a Web SDK project) — a global fixed-window limiter, 100 requests/minute per client IP,
      429 on rejection. Generous on purpose: this is abuse protection, not the mechanism for
      per-plan resource limits (`SubscriptionPlanLimits`, §9.9, already owns that job).
- [x] **CORS tightened, but still defaults open.** `Cors:AllowedOrigins` in config — if set,
      CORS restricts to exactly those origins; if empty (the shipped default), it falls back to
      `AllowAnyOrigin` for local Swagger/Postman use. Real client origins still need to be set
      before production traffic — the lever now exists, nothing is forcing anyone to pull it yet.
- [x] **Health-check endpoint.** `GET /health`, backed by a real `MongoHealthCheck` that pings
      the configured MongoDB (`{ ping: 1 }`) rather than just confirming the process is up.
      Smoke-tested: `200 Healthy` against the live Atlas cluster.
- [x] **Request/response logging** — covered by `UseSerilogRequestLogging()` above; no separate
      mechanism was needed.

### 9.12 Automated tests — unit tests done (2026-08-15), integration tests blocked
- [x] **Unit tests for Application-layer services.** New `tests/Vastora.Application.Tests`
      (xUnit + Moq), added to `Vastora.slnx`. A hand-written `FakeMongoRepository<T>`
      (`TestDoubles/`) backs every test instead of mocking `IMongoRepository<T>` directly — it
      compiles and evaluates the real `Expression<Func<T,bool>>` predicate against an in-memory
      `List<T>`, so tests exercise actual filtering/business-rule logic instead of just
      confirming a canned mock return value came back unchanged (a Moq stub can't do this: it
      has no way to *apply* a predicate, only to return whatever was configured for "any
      predicate"). 37 tests as of this session, covering `SubscriptionPlanLimits`,
      `BusinessService` (plan/type business-count caps), `UserService` (staff limits, delivery
      module gating), `ProductService` (product limits, variants, search/filter), `CategoryService`
      (tree building), `OrderService` (state machine transitions, delivery agent crediting,
      restocking, checkout delivery-fee fallback, ledger entries), `InventoryService`
      (movements, low-stock, valuation, adjustment type restrictions), and `AccountingService`
      (P&L date-windowing, balance sheet, expense CRUD). Run with `dotnet test` from the repo
      root. Not exhaustive — services untouched this session (Cart, Coupon, Auth's core login
      paths) have no new tests, which is honest backlog, not a claim of full coverage.
- [ ] **Integration tests against a real or Testcontainers-hosted MongoDB** — still not built.
      This session's environment had no Docker available, so there was nothing to build or run
      Testcontainers against; the unit suite above is real coverage, but it's not a substitute
      for testing against an actual MongoDB (predicate translation quirks — see §9.5's
      `Regex.IsMatch` note — are exactly the kind of thing only a real database catches).

### 9.13 Frontend
- [x] Blueprints written (2026-08-13) — see `docs/SUPEROFFICE_FRONTEND_BLUEPRINT.md`,
      `docs/BACKOFFICE_FRONTEND_BLUEPRINT.md`, `docs/ANTIVALY_SHOP_BLUEPRINT.md` (frontend
      implementation itself lives entirely in those self-contained docs, out of scope for this
      file — see the note at the top of this document). Code itself is still not started;
      revisit §9.1–9.2 gaps (validation, and the newer authorization pattern) as each frontend
      starts exercising the API for real, since a UI will surface gaps a curl smoke test won't.

### 9.14 Delivery module — make it optional per Business — done (2026-08-15)
- [x] `Business.DeliveryModuleEnabled` (`bool`, default `true`) on the `Business` entity (§5).
- [x] `PATCH /api/businesses/{businessId}/delivery-module` (`{ enabled: boolean }`, BusinessAdmin/
      TenantOwner/PlatformSuperAdmin — not BusinessStaff, same pattern as `PUT .../businesses/{id}`)
      to toggle it, via `IBusinessService.UpdateDeliveryModuleAsync`.
- [x] When disabled: `UserService.CreateStaffAsync` rejects `role: "DeliveryAgent"` with a
      `ConflictException` ("Delivery module is disabled for this business.") before creating the
      account, and `OrderService.AssignDeliveryAgentAsync` rejects the same way before touching the
      order — both check `Business.DeliveryModuleEnabled` first thing, via a new
      `IMongoRepository<Business>` dependency injected into each service. Existing `DeliveryAgent`
      staff and any order already assigned to one are untouched — the toggle only blocks *new*
      creation/assignment, confirmed by smoke test (see Progress Log).
- [x] Surfaced on `BusinessResponse` generally (not just the public storefront) — every endpoint
      that returns a Business, including `GET /api/shop/{businessSlug}`, now carries
      `deliveryModuleEnabled`, so BackOffice/SuperOffice/Shop frontends can all read it from the
      same field without a special case.
- **Not done, deliberately deferred:** an explicit "pickup fulfillment" `OrderStatus` value or
      alternate state-machine path for module-off Businesses — today staff can still move a
      module-off order's status manually through the existing `PATCH .../orders/{id}/status`
      (untouched by this change), which was judged sufficient for a first cut; revisit alongside
      §9.7's order status state machine if a dedicated status is ever needed.

### 9.15 Smart Inventory Management module — done (2026-08-15)
- [x] **9.15a — Stock movement ledger.** New `StockMovement` entity, and `IInventoryService.
      RecordMovementAsync` as the single place `Product.StockQuantity` is allowed to change —
      it updates the field and writes the audit row together. All three of the original
      mutation points now route through it: checkout (`Sale`, negative delta, referencing the
      order number — generated *before* the order is inserted specifically so it exists in time
      to use as the reference), order cancellation (`Return`, positive delta), and the new
      manual adjustment endpoint (9.15c). `GET .../products/{productId}/stock-movements` reads
      the trail back. Smoke-tested: a 2-unit checkout produced exactly one `Sale` movement with
      `quantityDelta: -2` and the real order number as its reference.
- [x] **9.15b — Reorder thresholds & low-stock alerts.** `Product.ReorderThreshold`/
      `ReorderQuantity` (both `int?`, null = no alert configured). `GET .../inventory/low-stock`
      lists tracked products at or below their threshold. No push notification yet — that's
      §9.10's `INotificationService` to wire up later, this just makes the signal queryable.
      Smoke-tested: set a threshold of 100 on a product with 43 in stock, it appeared in the
      low-stock list.
- [x] **9.15c — Manual stock adjustment endpoint.** `POST .../products/{productId}/stock-adjustments`
      (`AdjustStockRequest { QuantityDelta, Reason, Type }`), Admin-tier BackOffice roles, going
      through `RecordMovementAsync` like everything else. Rejects `Type: Sale`/`Return` with a
      403 (`ForbiddenException`) — those are system-generated only, from checkout/cancellation.
      This **replaces** the old silent overwrite in `ProductService.UpdateAsync`:
      `UpdateProductRequest` no longer has a `StockQuantity` field at all, so a general product
      edit (price, description, ...) can no longer accidentally reset stock as a side effect.
      Smoke-tested: a `-5` `DamageWriteOff` adjustment on a 48-unit product left it at 43.
- [x] **9.15d — Inventory valuation.** `GET .../inventory/valuation` —
      `Σ(StockQuantity × Price)`, overall and grouped by `CategoryId`. Feeds §9.16c's balance
      sheet as the inventory asset line. Smoke-tested: 43 units at $199.99 correctly valued at
      $8,599.57.

### 9.16 Accounting module — sales ledger & balance sheet — done (2026-08-15)
Single-entry, cash-basis bookkeeping, not double-entry/GAAP, as originally scoped.
- [x] **9.16a — Sales ledger.** New `LedgerEntry` entity — `Type` (`Revenue`/`Refund`, plus
      `DeliveryPayout`, added beyond the original spec — see the note below), `Amount`,
      `Currency`, `ReferenceOrderId` (the order number), `OccurredAt`. Written only by
      `OrderService`, never by a controller: `Revenue` on the `→ Delivered` transition,
      `Refund` on `→ Refunded`. No `PaymentStatus` check needed for the refund case — the
      §9.7 state machine already guarantees `Refunded` is only reachable from `Delivered`, so a
      matching `Revenue` entry always exists to offset. Smoke-tested: a $412.48 delivered order
      produced exactly that `Revenue` entry.
      **Deviation from the original spec, and why:** `DeliveryAgentProfile.Balance` (§9.7) is
      only a running total with no per-payout history, so it can't be sliced by date range for
      a P&L. Added `LedgerEntryType.DeliveryPayout`, written alongside each agent credit, so
      §9.16c's P&L can attribute payouts to the window they actually happened in — this is
      what 9.16d asked for, just implemented as part of 9.16a instead of bolted on separately.
- [x] **9.16b — Manual expense tracking.** New `Expense` entity, full BackOffice CRUD
      (`GET/POST/PUT/DELETE .../expenses`), Admin-tier only (§9.3's reasoning — financial data).
      Smoke-tested: created, the P&L in the same window picked it up correctly.
- [x] **9.16c — P&L and balance sheet reports.** `GET .../accounting/profit-and-loss?from&to`
      sums `LedgerEntry`/`Expense` rows inside `[from, to]`: `NetProfit = Revenue − Refunds −
      Expenses − DeliveryPayouts`. `GET .../accounting/balance-sheet` is all-time (a snapshot,
      not a period) `CashPosition` plus §9.15d's current inventory valuation as `TotalAssets` —
      no liabilities tracked, so this is a partial balance sheet, stated as such in the DTO's
      own doc comment. Both computed on the fly, not materialized. Smoke-tested end to end
      against the live data from the other tests in this session: P&L returned `revenue:
      412.48, refunds: 0, expenses: 100, deliveryPayouts: 20, netProfit: 292.48` — arithmetic
      confirmed by hand; balance sheet returned `cashPosition: 292.48, inventoryValue: 8599.57,
      totalAssets: 8892.05`, consistent with the P&L run moments earlier.
- [x] **9.16d — Delivery agent balance as a ledger line.** Folded into 9.16a above (see the
      deviation note) rather than built as a separate step — `DeliveryPayout` entries exist and
      flow into the P&L now.

---

## 10. Progress Log

Newest entry first. Keep entries short — what happened and why, not a diff.

### 2026-08-15 — Roadmap §9.3–§9.16: the rest of the backend roadmap in one session
Asked to work through every remaining roadmap module in order; did §9.3 through §9.16 (skipping
nothing except what's explicitly still blocked). Full technical detail lives in each §9.x entry
above — this entry is the narrative summary and the honesty ledger for what wasn't done.

**Built:** §9.3 (Staff vs Admin permission split — hardcoded roles, not a configurable system,
matching every other authorization check in this codebase), §9.4 (Platform-driven tenant type
change; self-serve request flow still a product decision), §9.5 (category trees, catalog-only
product variants, local-disk image upload behind an `IFileStorageService` swap point, server-side
search via `Regex.IsMatch`), §9.6 (manual payment recording for the COD flow that's all that
exists; real gateway still needs a chosen provider), §9.7 (delivery fee defaults, an order status
state machine, delivery agent earnings), §9.8 (SuperOffice cross-business analytics), §9.10
(a full password-reset flow, plus `INotificationService` with a logging-only implementation —
no real email/SMS provider chosen), §9.11 (Serilog, rate limiting, configurable-but-still-open
CORS, a real MongoDB-backed health check), §9.12 (a new `tests/Vastora.Application.Tests` xUnit
project — 37 tests — built around a hand-written `FakeMongoRepository<T>` rather than mocking the
repository interface, since a mock can't apply a predicate the way a real query engine does),
§9.15 (a full stock-movement audit ledger replacing every direct `StockQuantity` write, reorder
alerts, a manual adjustment endpoint, inventory valuation), and §9.16 (a sales ledger, expense
tracking, P&L and balance-sheet reports — `LedgerEntryType.DeliveryPayout` was added beyond the
original spec so agent payouts could be date-windowed in the P&L, which folded 9.16d into 9.16a
rather than building it as a separate step later).

**Deliberately not built, and why:** a real payment gateway (§9.6) and real email/SMS delivery
(§9.10) both need a provider chosen — a product decision, not something to fabricate credentials
for. A self-serve Tenant type-upgrade request flow (§9.4) needs the same kind of product decision
about the approval process. Testcontainers-hosted MongoDB integration tests (§9.12) needed Docker,
which wasn't available in this session's environment — the unit suite is real coverage but isn't
a substitute for that. Cart/Order variant selection (§9.5) was scoped out on purpose: threading
variants through `CartItem`/`OrderItem` is a materially bigger redesign than adding them to the
catalog, and half-building it would have been worse than stating the boundary clearly.

**Verified, and how:** every new business-rule-bearing service got unit tests first
(37 total, `dotnet test` from the repo root). Then a single fresh tenant was smoke-tested live
against the Atlas database across nearly every new endpoint in one connected flow — category
tree, product variants, image upload (round-tripped an actual PNG through the static file
server), the Staff-vs-Admin 403s, checkout with a Business's default delivery fee, an illegal
order-status transition rejected then the real legal path walked through to `Delivered`
(confirming the assigned agent's balance credited and a `DeliveryAgentProfile` update), a manual
payment-status update, SuperOffice analytics, the full password-reset loop (request → pull the
token from the server log → reset → confirm the old refresh token was revoked → confirm the new
password logs in → confirm the reset token can't be reused), stock movements/low-stock/valuation/
adjustment, and expense creation feeding into a P&L and balance sheet whose arithmetic was
checked by hand against the exact orders/expenses seeded during the same session. The one
category of thing *not* independently curled — `PATCH /api/platform/tenants/{id}/type` and the
Platform-only usage mirror from §9.9 — is the same PlatformSuperAdmin-credentials gap noted in
§9.9's entry below: this session never had that password. Recorded as such in §9.4 rather than
claimed as tested.

**Docs:** this entry plus every §9.x section above were updated in place (not appended
separately) so the roadmap stays the single source of truth rather than drifting from a
progress-log narrative. §5's domain model table, §4's tech stack, and §7's endpoint inventory and
"deliberate simplifications" list were all rewritten to match current reality. The three frontend
blueprints got their own pass in the same session — see their own "self-contained" headers for
what changed; summarized in the frontend docs, not duplicated here per this file's own intro note
on keeping frontend content out of this document.

### 2026-08-15 — Roadmap §9.9: subscription feature-gating and usage metering
Implemented the two tractable pieces of §9.9 — plan-based feature-gating and usage metering —
and deliberately left the third (actual billing/payment integration) alone, since it's still
blocked on §9.6's unmade gateway decision and there's nothing legitimate to build without real
Stripe/SSLCommerz/bKash credentials to integrate against. Added `SubscriptionPlanLimits.For(plan)`
(`Vastora.Application/Tenants/`), a static per-plan lookup (`MaxBusinesses`/`MaxStaffPerBusiness`/
`MaxProductsPerBusiness`, `null` = unlimited) with Trial/Starter/Growth/Enterprise tiers, and wired
it into the three creation paths that should actually respect a plan: `BusinessService.CreateAsync`
(on top of, not instead of, the existing `SingleBusiness`-type cap — a `MultiBusiness` tenant is
now also capped by its plan, not just unlimited), `UserService.CreateStaffAsync` (staff per
Business, all three staff roles counted together), and `ProductService.CreateAsync` (products per
Business) — all three 409 with a plan-specific message before creating anything over the limit.
Added usage metering as a new `ITenantService.GetUsageAsync`, surfaced via
`GET /api/tenants/me/usage` (TenantOwner) and `GET /api/platform/tenants/{tenantId}/usage`
(PlatformSuperAdmin) — both return `TenantUsageResponse`: plan, Business count vs. limit, and a
per-Business breakdown of staff/product counts vs. limit.

Smoke-tested against the live Atlas database with a fresh Trial-plan `MultiBusiness` tenant:
created exactly 3 staff before a 4th 409'd ("...allows up to 3 staff member(s)..."), created
exactly 20 products before a 21st 409'd, attempted a 2nd Business and got the *new* plan-based
409 specifically (not the older type-based one, confirmed by message text, since this tenant's
`MultiBusiness` type would otherwise have allowed it), and `GET /api/tenants/me/usage` reported
back the exact same numbers (`1/1` Businesses, `3/3` staff, `20/20` products). Could not
independently test the Platform-only usage endpoint or the pre-existing `PATCH .../plan` upgrade
path — no PlatformSuperAdmin credentials were available this session (the seeded admin's
password was generated once at kickoff and was never captured anywhere retrievable). Recorded
that gap explicitly in §9.9 rather than claiming a verification that didn't happen; both share
code that *was* proven correct through the TenantOwner-facing route.

Updated the two frontend docs that touch this: SuperOffice got `GET /api/tenants/me/usage`
documented in full (§6.2) plus an update to the existing "Gating the Add Business button" note,
since a plan limit can now block business creation independently of the `SingleBusiness`/
`MultiBusiness` type check it already covered. BackOffice got 409 notes on both the staff (§7.6)
and product (§7.4) creation rows, plus an honest callout that `BusinessAdmin`/`BusinessStaff`
can't call the usage endpoint to check proactively (`TenantOwner`-only) — so BackOffice can only
react to the 409, which is now explicit instead of a silent gap. Antivaly Shop wasn't touched:
none of these limits apply to customer-facing checkout/registration.

### 2026-08-15 — Reorganized: this file is backend/API only now
No API or code changes this pass — pure documentation reorganization. Removed the old
§10 "Frontend projects" section: it was a redundant index (Doc/App/Stack/Audience table +
a "keep these in sync" paragraph) whose content already lived in each of the three frontend
blueprints' own opening sections (each states its own stack and audience under "What this app
is") — there was nothing frontend-specific here that wasn't already better placed in
`docs/SUPEROFFICE_FRONTEND_BLUEPRINT.md`, `docs/BACKOFFICE_FRONTEND_BLUEPRINT.md`, or
`docs/ANTIVALY_SHOP_BLUEPRINT.md`, so nothing needed to be migrated, just deleted. Replaced it
with a short paragraph in this file's intro (top of document) stating that frontend
implementation is out of scope here and pointing to the three docs, plus the one instruction
worth keeping in a backend-facing file: update the relevant frontend doc in the same session a
backend API change lands. The old Progress Log became §10 (was §11). Fixed the resulting
dangling `§10` cross-reference in the live §9.13 roadmap checklist and in all three frontend
docs' own "self-contained" headers (they pointed back at "`VASTORA_BLUEPRINT.md`, §10" — now
point at "the intro note" instead, since the target is no longer a numbered section). Left the
handful of `§10` mentions inside *historical* Progress Log entries below untouched — they were
accurate descriptions of the document at the time they were written, and progress log entries
are a record, not living cross-references. Audited the rest of this file for other frontend-only
content that should have moved (screen breakdowns, token-storage strategy, deployment configs,
etc.) and found none — every other "frontend" mention here is either scope framing (§1/§2: "no
frontend exists yet in this repo") or a roadmap/API-design note explaining a backend decision in
terms of its frontend consumers, which is legitimately backend content and stayed put.

### 2026-08-15 — Roadmap §9.14: Delivery module made optional per Business
Implemented §9.14 end to end. Added `Business.DeliveryModuleEnabled` (`bool`, default `true`)
to the domain entity, surfaced it on `BusinessResponse` (so it rides along on every endpoint
that already returns a Business, public storefront included, no special-casing needed), and
added `PATCH /api/businesses/{businessId}/delivery-module` (`BusinessesController` →
`IBusinessService.UpdateDeliveryModuleAsync`), gated to the same Admin/TenantOwner/Platform set
as `PUT .../businesses/{id}` — BusinessStaff excluded, matching the existing pattern for
destructive Business-level actions. Enforcement lives at the two places that actually create new
delivery obligations: `UserService.CreateStaffAsync` now rejects `role: "DeliveryAgent"` with a
409 when the flag is off (before creating the account or its `DeliveryAgentProfile`), and
`OrderService.AssignDeliveryAgentAsync` rejects the same way before touching the order — both
gained a new `IMongoRepository<Business>` constructor dependency to check the flag. Deliberately
left everything else alone: existing `DeliveryAgent` accounts, in-flight assignments, and the
plain `PATCH .../orders/{id}/status` endpoint all keep working with the module off, since the
goal was blocking new delivery work, not tearing down old.

Smoke-tested the full flow against the live Atlas database with a fresh tenant: created a
`DeliveryAgent` while enabled (200), disabled the module (200, `deliveryModuleEnabled: false`
in the response), re-attempted staff creation with `role: "DeliveryAgent"` (409, the exact
message), hit `assign-delivery` on a bogus order while disabled (409 — the module check fires
*before* the order lookup, confirmed by re-enabling and hitting the same bogus order again,
which then correctly 404'd instead), re-enabled the module (200, flag back to `true`), created
a third agent successfully, and confirmed the public `GET /api/shop/{slug}` storefront response
carries the flag. Updated the three frontend blueprint docs (§10): SuperOffice and BackOffice
both got the new endpoint documented (BackOffice also got inline notes at the staff-creation and
assign-delivery rows suggesting the UI hide those actions rather than let them 409), and Antivaly
Shop got a note on using the flag to suppress delivery-related storefront copy for pickup-only
sellers. Also corrected the roadmap checklist itself: the original 9.14 draft mentioned blocking
"status-activation" for delivery agents, but `DeliveryAgentStatus` (Free/Busy/Offline/Blocked) is
an operational availability toggle, not an account-lifecycle concept — there was nothing there to
gate, so that line was dropped rather than implemented as written.

### 2026-08-15 — Roadmap §9.1: request validation wired up, plus four new modules planned
Implemented §9.1: added `ValidationActionFilter` (`Vastora.API/Filters/`), a global MVC action
filter registered in `Program.cs` that resolves `IValidator<T>` for each action argument from DI
and runs it before the action body executes, throwing one combined
`FluentValidation.ValidationException` on failure — `ExceptionHandlingMiddleware` already knew
how to turn that into a 400, so this was purely additive, no middleware change. Smoke-tested
against the live Atlas database: `POST /api/tenants/signup` with a malformed email and a
7-character password now 400s with FluentValidation's own per-field messages (distinct from
`[ApiController]`'s separate built-in check for missing/null fields, verified to still fire
first and independently), `POST /api/businesses/{id}/categories` with an empty `name` 400s, and
both endpoints still succeed end-to-end on valid input — a fresh tenant/business/category were
created during the test. Updated §4 and §7 to drop the "not yet wired up" language, and updated
the three frontend blueprint docs (§10) to drop their matching "known gap" callouts, since none
of the three frontends had started consuming the API yet and no DTO/contract shape changed.

Also reviewed the domain model against a request for five specific features and updated the
Roadmap accordingly. Two were already fully modeled and just needed the plan to say so
explicitly: category→subcategory already lives in one `Category` collection via a nullable
`ParentCategoryId` (§9.5), and `Product.Images` is already a `List<string>` supporting multiple
pictures per product (§9.5) — both just need their read/upload endpoints built, no schema
change. Three were genuinely new and got their own roadmap sections, each pre-split into
session-sized sub-items so no single future session inherits a multi-week block: §9.14 (make
the Delivery module opt-out per Business, since pickup-only/third-party-courier sellers have no
use for it today), §9.15 (Smart Inventory Management — a stock-movement audit ledger, low-stock
reorder thresholds, a logged manual-adjustment endpoint, and inventory valuation), and §9.16
(Accounting — an auto-generated sales ledger off order fulfillment, manual expense tracking,
P&L/balance-sheet reports, and eventually folding delivery-agent payouts in as an expense line).
Added a paragraph at the top of §9 spelling out a suggested interleaved session order across the
new and existing backlog, so the workload stays balanced rather than front-loading three large
modules back to back.

### 2026-08-13 — Swagger polish + enum-as-string + frontend blueprints
Swagger was already live from the foundation session (`Swashbuckle`, `/swagger`); this pass
polished it ahead of three frontend teams starting to consume the API: added `[Tags(...)]` to
all 15 controllers so the UI groups by area (Auth / Tenant Onboarding / Platform / SuperOffice
/ BackOffice - * / Shop - *) instead of one flat 47-endpoint list, enabled XML doc comment
generation (`GenerateDocumentationFile`) so controller-level `<summary>` text shows up in the
UI, and added a `JsonStringEnumConverter` globally — every enum (`UserRole`, `OrderStatus`,
`TenantType`, etc.) now reads and writes as its name (`"TenantOwner"`) instead of its
underlying int (`2`), both in normal DTOs and in the handful of endpoints whose request body
is a bare enum. This was a deliberate fix-before-documenting: writing three frontend API
contracts around `"role": 2`-style magic numbers would have been bad DX baked into every
future session that reads those docs. Verified with curl: tenant signup accepting
`"tenantType": "SingleBusiness"` and returning `"role": "TenantOwner"`, and
`PATCH .../products/{id}/status` accepting the bare JSON string `"Active"` as its body.
Then wrote the three frontend blueprint docs listed in §10.

### 2026-08-13 — Roadmap §9.2: resource-based authorization
Replaced the manual `VastoraControllerBase.EnsureBusinessAccessAsync` check (called
imperatively at the top of every BackOffice action) with a real ASP.NET Core
`IAuthorizationHandler`: `BusinessMemberRequirement` + `BusinessAccessAuthorizationHandler`
in `Vastora.API/Authorization/`, wired up as the `"BusinessMember"` policy in `Program.cs` and
applied declaratively via `[Authorize(Policy = "BusinessMember")]` on the 7 controllers that
had used the old helper (`BusinessesController`, `StaffController`, `CategoriesController`,
`ProductsController`, `CouponsController`, `DeliveryAgentsController`, `OrdersController`).
The handler reads the `{businessId}` route value directly, applies the same three-role scoping
rules as before, and stashes the resolved real `TenantId` on `HttpContext.Items` for the action
to read back via a new synchronous `VastoraControllerBase.ResolvedTenantId` property — so
actions that only needed the access check (most `GetAll`/`GetById` reads) dropped the call
entirely, and actions that needed the tenantId for a service call dropped the `await` and the
now-unnecessary `IBusinessService` constructor parameter in five of the seven controllers
(`BusinessesController` and the `SuperOfficeController` still need `IBusinessService` for
unrelated reasons). Re-ran the full smoke-test suite against the live Atlas database with two
fresh tenants: same-tenant access (200), cross-tenant `TenantOwner`→`TenantOwner` access (404,
unchanged), cross-tenant `BusinessAdmin`→`BusinessAdmin` access (403, the other code branch),
unauthenticated (401), bogus `businessId` (404), `PlatformSuperAdmin` cross-tenant access
(200), and a full write (`Create` then `Update` a Category) to confirm the resolved `TenantId`
threaded through correctly end-to-end. All behavior is unchanged from the manual version;
only the mechanism moved to the framework's authorization pipeline, per §9.2's original ask.

### 2026-08-13 — Foundation session
Studied `Antivaly-main.zip` (legacy single-shop ASP.NET Web API 2 / EF6 project) to understand
the domain shape being generalized. Scaffolded the full Vastora .NET 10 solution (Domain /
Application / Infrastructure / API, Clean Architecture). Built the multi-tenant domain model
(Tenant → Business → Users/Catalog/Orders), MongoDB persistence via a generic repository, JWT
auth with hashed/rotating refresh tokens, BCrypt password hashing, and the full foundation API
surface listed in §7 (tenant onboarding, BackOffice catalog/staff/order management, SuperOffice,
Platform admin, public Shop browsing, Customer cart/checkout). Connected to the real MongoDB
Atlas cluster provided at kickoff, seeded a PlatformSuperAdmin, and smoke-tested the entire
golden path end-to-end with `curl` (tenant signup → product creation → public browsing →
customer registration → cart → checkout → stock decrement → BackOffice order visibility →
cross-tenant access denial). Wrote this blueprint. Everything in §7 is real and verified;
everything in §9 is planned.
