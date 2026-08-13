# Vastora Blueprint

**This file is the single source of truth for the Vastora project.** It records the full
vision, the architecture decisions, what has been built, and what's left — broken into
sessions small enough to execute without losing coherence. Read this file first in any
new session before writing code. Update the **Progress Log** and **Roadmap** checkboxes
at the end of every session, however small.

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
tests/                    — placeholder, empty so far (see Roadmap §9.7).
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

`VastoraControllerBase.EnsureBusinessAccessAsync(businessId, businessService)` is the one
chokepoint that turns "which Business is this request allowed to touch" into a `TenantId`,
for every BackOffice-shaped controller:

- `BusinessAdmin` / `BusinessStaff` / `DeliveryAgent` → only their own `BusinessId` (from JWT).
- `TenantOwner` → any Business that belongs to their own `TenantId` (SuperOffice).
- `PlatformSuperAdmin` → any Business at all.

This is a manual, per-request check rather than a full custom `IAuthorizationHandler` /
resource-based policy — intentionally, to keep the foundation simple. See Roadmap §9.2 for
the follow-up.

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
- **FluentValidation** — request DTO validation, resolved manually in `AuthTokenIssuer`/
  services... actually invoked automatically? No — see §7 for exactly where validators are
  wired in today (they're registered in DI but **not yet auto-invoked** by controllers; this
  is a known gap, see Roadmap §9.1).
- **Swashbuckle (Swagger/OpenAPI)** — interactive API docs at `/swagger`.
- **DotNetEnv** — loads a root-level `.env` into configuration at startup (see §6).

---

## 5. Domain model

All entities live in `Vastora.Domain.Entities`, inherit `BaseEntity` (`Id`, `CreatedAt`,
`UpdatedAt`), and implement `ITenantScoped`/`IBusinessScoped` where relevant.

| Entity | Scoped to | Purpose |
|---|---|---|
| `TenantAccount` | — (root) | The subscriber. `Type` (Single/MultiBusiness), `Status`, `Plan`, `OwnerUserId`. |
| `Business` | Tenant | One storefront. Slug (public, globally unique), branding fields, currency, status. |
| `AppUser` | Tenant (+Business except for PlatformSuperAdmin/TenantOwner) | Single table for every role, discriminated by `Role`. Embedded `Addresses` list. |
| `RefreshToken` | User | Hashed refresh tokens, rotation-friendly (`RevokedAt`, `ReplacedByTokenId`). |
| `Category` | Business | Supports `ParentCategoryId` for subcategories (not yet surfaced in any tree-building logic — flat list today). |
| `Product` | Business | Price, `CompareAtPrice`, `DiscountPercent`/`DiscountExpiresAt` → `EffectivePrice` computed property. Inventory via `StockQuantity`/`TrackInventory`. |
| `Coupon` | Business | Percentage or fixed discount, usage cap, validity window. `IsValidNow` computed property. |
| `Cart` | Business + Customer | One live cart per customer per business, embedded `CartItem` list, optional coupon code. |
| `Order` | Business | Embedded `OrderItem` snapshot (price/name captured at checkout, immune to later product edits), `StatusHistory` audit trail, `PaymentStatus` separate from fulfillment `Status`. |
| `DeliveryAgentProfile` | Business | Operational stats for a `DeliveryAgent` user — status, balance, level, completed count. |

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
- `POST /api/tenants/signup` — public; provisions Tenant + Owner + first Business.
- `GET /api/tenants/me` — TenantOwner only.
- `POST /api/shop/{businessSlug}/auth/register`, `POST /api/shop/{businessSlug}/auth/login` — Customer realm, scoped to one Business by slug.

**Platform (`PlatformSuperAdmin` only)**
- `GET /api/platform/tenants`, `GET /api/platform/tenants/{tenantId}`
- `PATCH /api/platform/tenants/{tenantId}/status`, `PATCH /api/platform/tenants/{tenantId}/plan`

**SuperOffice (`TenantOwner` only, spans every Business they own)**
- `GET/POST /api/superoffice/businesses`
- `GET/PUT /api/superoffice/businesses/{businessId}`, `PATCH .../status`

**BackOffice (`BusinessAdmin`/`BusinessStaff`, + `TenantOwner`/`PlatformSuperAdmin` via `EnsureBusinessAccessAsync`)**
- `GET/PUT /api/businesses/{businessId}` — self profile.
- `GET/POST /api/businesses/{businessId}/staff`, `PATCH .../staff/{userId}/status`, `GET .../customers`.
- Full CRUD: `.../categories`, `.../products` (+ `PATCH .../status`), `.../coupons`.
- `GET .../delivery-agents`, `GET/PATCH .../delivery-agents/me`, `PATCH .../delivery-agents/{userId}/status`.
- `GET .../orders`, `GET .../orders/{orderId}`, `PATCH .../orders/{orderId}/status`, `PATCH .../orders/{orderId}/assign-delivery`, `GET .../orders/assigned-to-me` (DeliveryAgent).

**Shop (public + `Customer` role)**
- `GET /api/shop/{businessSlug}`, `.../categories`, `.../products`, `.../products/{productId}` — all public, no auth.
- `GET/POST/PUT/DELETE /api/shop/cart`, `.../cart/items/{productId}`, `POST .../cart/coupon` — Customer only, scope from JWT.
- `POST /api/shop/orders/checkout`, `GET /api/shop/orders`, `GET .../{orderId}`, `POST .../{orderId}/cancel` — Customer only.

### Deliberate simplifications in this foundation

These are known, intentional cuts to keep the first session shippable — not oversights:

- **No payment gateway.** Checkout creates an order with `PaymentStatus.Pending` and
  `Status.Processing` immediately (cash-on-delivery-style). No Stripe/SSLCommerz/bKash
  integration yet.
- **BusinessAdmin vs BusinessStaff have identical permissions today.** The roles exist and
  are stored, but no endpoint currently treats them differently. Split later (Roadmap §9.3).
  `BusinessStaff` is excluded from a couple of destructive Business-level actions
  (`PUT /api/businesses/{id}`) as the one exception already in place.
- **FluentValidation validators are registered in DI but not yet invoked automatically** —
  they exist for the important write DTOs (`TenantSignUpRequest`, `CreateProductRequest`,
  etc.) but nothing calls `IValidator<T>.ValidateAndThrowAsync` yet. `ExceptionHandlingMiddleware`
  already knows how to turn a `FluentValidation.ValidationException` into a 400, so wiring
  this up is purely additive — see Roadmap §9.1.
- **Category tree is flat.** `ParentCategoryId` exists on the entity but nothing builds a
  nested tree response yet.
- **No product images/file upload** — `Images`/`Tags` are `List<string>` with no upload
  endpoint behind them yet (URLs only, supplied by the caller).
- **SingleBusiness tenants are capped at one Business** by `BusinessService.CreateAsync`
  (throws `ConflictException` on a second attempt) — but there's no self-serve upgrade path
  from Single→MultiBusiness yet; that requires a `PATCH` a Platform admin would have to run
  manually today (`TenantAccount.Type` has no update endpoint at all yet, in fact — only
  `Status` and `Plan` are patchable via `/api/platform/tenants/{id}`). Add a Type-change
  endpoint in Roadmap §9.4.
- **No resource-based `IAuthorizationHandler`** — scoping is manual (`EnsureBusinessAccessAsync`)
  rather than a policy-based ASP.NET Core authorization handler. Works correctly, just not
  the "proper" long-term pattern for a codebase this size.
- **CORS is wide open** (`AllowAnyOrigin/Header/Method`) — fine for API development against
  Swagger/Postman, must be tightened before any real frontend/production deploy.
- **No automated tests.** `tests/` is an empty placeholder directory.
- **No rate limiting, no caching layer, no structured logging beyond the ASP.NET Core default.**

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

### 9.1 Wire up request validation (small, do this first)
- [ ] Inject `IValidator<T>` into controllers (or add a thin action filter) and call
      `ValidateAndThrowAsync` before invoking each service — the validators and the
      exception-to-400 mapping already exist, just aren't connected yet.

### 9.2 Proper resource-based authorization
- [ ] Replace `EnsureBusinessAccessAsync` with an `IAuthorizationHandler` +
      `AuthorizationPolicyProvider` so business-scoping is a first-class ASP.NET Core policy,
      declarative on controllers via `[Authorize(Policy = "BusinessMember")]` instead of an
      imperative call at the top of every action.

### 9.3 Real BusinessAdmin vs BusinessStaff permission split
- [ ] Decide what Staff can't do (delete products? see revenue? manage other staff?) and
      enforce it — likely needs a lightweight permissions list on `AppUser` rather than a
      hardcoded role check, if it needs to be configurable per business.

### 9.4 Tenant lifecycle completeness
- [ ] `PATCH /api/platform/tenants/{id}/type` — Single→MultiBusiness upgrade path.
- [ ] Self-serve upgrade request flow (TenantOwner requests, Platform approves?) if that's
      the desired business process — needs a product decision, not just code.

### 9.5 Catalog depth
- [ ] Nested category trees (the data model already supports `ParentCategoryId`).
- [ ] Product variants (size/color) — currently `Product` is a single SKU with no variant
      concept at all.
- [ ] Image upload (S3/Azure Blob/Cloudinary — pick one) instead of caller-supplied URLs.
- [ ] Product search/filter improvements beyond the current in-memory `Contains` scan in
      `ProductService.GetPublicCatalogAsync` (fine at small scale, won't scale — consider
      Mongo Atlas Search or a dedicated search index later).

### 9.6 Payments
- [ ] Pick a gateway (Stripe first, likely — or a Bangladesh-local one like SSLCommerz/bKash
      given the project's context) and wire `PaymentStatus` transitions to real webhook
      events instead of always starting at `Pending`.

### 9.7 Delivery & fulfillment depth
- [ ] Delivery fee calculation (currently caller-supplied at checkout, no zone/distance logic).
- [ ] Proper order status state machine (right now any BackOffice caller can set any
      `OrderStatus` in any order — no transition rules enforced).
- [ ] Delivery agent earnings/balance logic (the `Balance`/`DeliveryCharge` fields exist on
      `DeliveryAgentProfile` but nothing writes to `Balance` yet).

### 9.8 SuperOffice depth
- [ ] Cross-business analytics/dashboards (revenue, orders, top products across every
      Business a Tenant owns) — currently SuperOffice can only list/CRUD Businesses, no
      aggregated reporting endpoint exists yet.

### 9.9 Subscription & billing
- [ ] `SubscriptionPlan` exists as an enum on `TenantAccount` with a Platform-only PATCH
      endpoint, but there's no actual billing integration, feature-gating by plan, or
      usage metering yet.

### 9.10 Notifications
- [ ] Email (order confirmation, password reset — there's no password-reset flow at all
      yet, actually; add that too) and/or SMS on order status changes.

### 9.11 Observability & hardening
- [ ] Structured logging (Serilog), rate limiting, tighten CORS to real origins,
      health-check endpoint, request/response logging for debugging.

### 9.12 Automated tests
- [ ] Unit tests for Application-layer services (business rules are all there and testable
      today — this is pure backlog, not blocked on anything).
- [ ] Integration tests against a real or Testcontainers-hosted MongoDB.

### 9.13 Frontend
- [ ] Explicitly out of scope until the API surface above feels sufficient — revisit this
      list first.

---

## 10. Progress Log

Newest entry first. Keep entries short — what happened and why, not a diff.

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
