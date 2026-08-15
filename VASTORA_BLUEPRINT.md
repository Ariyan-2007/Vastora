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
- **Swashbuckle (Swagger/OpenAPI)** — interactive API docs at `/swagger`, grouped into 15 tag
  sections (Auth, Tenant Onboarding, Platform, SuperOffice, BackOffice - *, Shop - *) so a
  47-endpoint surface stays navigable. XML doc comments on controllers are picked up
  automatically (`GenerateDocumentationFile` in `Vastora.API.csproj`).
- **DotNetEnv** — loads a root-level `.env` into configuration at startup (see §6).
- **Enums serialize as strings, not numbers** — `JsonStringEnumConverter` is registered
  globally (`Program.cs`), both directions. A `Product.Status` reads/writes as `"Active"`,
  never `2`. This applies everywhere, including the handful of endpoints whose request body
  *is* a bare enum (e.g. `PATCH /api/businesses/{id}/products/{id}/status` takes the JSON
  string `"Active"` directly as its body, not an object wrapping it).

---

## 5. Domain model

All entities live in `Vastora.Domain.Entities`, inherit `BaseEntity` (`Id`, `CreatedAt`,
`UpdatedAt`), and implement `ITenantScoped`/`IBusinessScoped` where relevant.

| Entity | Scoped to | Purpose |
|---|---|---|
| `TenantAccount` | — (root) | The subscriber. `Type` (Single/MultiBusiness), `Status`, `Plan`, `OwnerUserId`. |
| `Business` | Tenant | One storefront. Slug (public, globally unique), branding fields, currency, status, `DeliveryModuleEnabled` (default `true` — see §9.14). |
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
- `PATCH /api/businesses/{businessId}/delivery-module` — toggle `DeliveryModuleEnabled` (Admin/TenantOwner/Platform, not Staff; added 2026-08-15, §9.14).
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
- **Category tree is flat.** Categories and subcategories already share one `Category`
  collection — `ParentCategoryId` is `null` for a top-level category and set to the parent's
  `Id` for a subcategory, no separate table (see §5) — but nothing builds a nested tree
  response from that yet; `GetForBusinessAsync` returns a flat list today. See Roadmap §9.5.
- **Multiple product images are already modeled, just not uploadable.** `Product.Images` is a
  `List<string>` (see §5) — a product can already carry several picture URLs — but there is no
  file upload endpoint behind it yet, only caller-supplied URLs. See Roadmap §9.5.
- **SingleBusiness tenants are capped at one Business** by `BusinessService.CreateAsync`
  (throws `ConflictException` on a second attempt) — but there's no self-serve upgrade path
  from Single→MultiBusiness yet; that requires a `PATCH` a Platform admin would have to run
  manually today (`TenantAccount.Type` has no update endpoint at all yet, in fact — only
  `Status` and `Plan` are patchable via `/api/platform/tenants/{id}`). Add a Type-change
  endpoint in Roadmap §9.4.
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

§9.14–9.16 (Delivery module toggle, Smart Inventory Management, Accounting) were added
2026-08-15 and are each pre-split into session-sized sub-items (9.15a/b/c/d, 9.16a/b/c/d) for
exactly this reason — don't do "all of 9.15, then all of 9.16" as two marathon sessions.
Suggested interleave, updated as items land: ~~9.14~~ (done 2026-08-15) → 9.5's category-tree
half → 9.15a → 9.16a → 9.3 → 9.15b → 9.16b → 9.5's image-upload half → 9.15c → 9.16c → 9.15d →
9.16d, picking up other backlog items between them as they become useful. 9.15a should land
before 9.15c (adjustment needs the ledger to log to), and 9.15d before 9.16c (valuation feeds
the balance sheet) — otherwise order freely.

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

### 9.3 Real BusinessAdmin vs BusinessStaff permission split
- [ ] Decide what Staff can't do (delete products? see revenue? manage other staff?) and
      enforce it — likely needs a lightweight permissions list on `AppUser` rather than a
      hardcoded role check, if it needs to be configurable per business.

### 9.4 Tenant lifecycle completeness
- [ ] `PATCH /api/platform/tenants/{id}/type` — Single→MultiBusiness upgrade path.
- [ ] Self-serve upgrade request flow (TenantOwner requests, Platform approves?) if that's
      the desired business process — needs a product decision, not just code.

### 9.5 Catalog depth
- [ ] Nested category trees. **Note: category → subcategory is already one table, not two** —
      `Category.ParentCategoryId` (§5) is `null` for a top-level category and points at the
      parent's `Id` for a subcategory, exactly the shape requested when this item was reviewed
      (2026-08-15). What's missing is purely the read side: `ICategoryService` needs a
      tree-building method (group by `ParentCategoryId`, nest recursively) and a
      `GET .../categories/tree` (or a `?asTree=true` flag on the existing list endpoint) —
      no schema change required.
- [ ] Product variants (size/color) — currently `Product` is a single SKU with no variant
      concept at all.
- [ ] Image upload (S3/Azure Blob/Cloudinary — pick one) for multiple product pictures.
      **Note: `Product.Images` (§5) is already a `List<string>`, so a product can already hold
      several picture URLs** — reviewed 2026-08-15, no domain change needed. What's missing is
      an actual upload endpoint (`POST .../products/{id}/images`, multipart) that stores a file
      and appends its URL to the list, instead of requiring the caller to already have a
      hosted URL for every picture.
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
- [x] Blueprints written (2026-08-13) — see §10. Code itself is still not started; revisit
      §9.1–9.2 gaps (validation, and the newer authorization pattern) as each frontend starts
      exercising the API for real, since a UI will surface gaps a curl smoke test won't.

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

### 9.15 Smart Inventory Management module (added 2026-08-15)
Today stock is a single mutable `Product.StockQuantity` int, written from three different places
with no audit trail: checkout decrements it (`OrderService`, line ~46), order cancellation restores
it (`OrderService`, line ~187), and a plain product edit overwrites it outright
(`ProductService.UpdateAsync`). None of those writes are logged, and there's no reorder/low-stock
signal at all. Sized as four separate sessions, in dependency order:
- [ ] **9.15a — Stock movement ledger.** New `StockMovement` entity (Business/Tenant-scoped):
      `ProductId`, `Type` (`Sale`/`Restock`/`Return`/`Adjustment`/`DamageWriteOff`), `QuantityDelta`,
      `Reason`, `ReferenceOrderId?`, `CreatedByUserId`. Route the three existing mutation points
      above through a single `IInventoryService.RecordMovementAsync` that both updates
      `Product.StockQuantity` and writes the movement, instead of touching the field directly.
      `GET .../products/{productId}/stock-movements` to read the trail back.
- [ ] **9.15b — Reorder thresholds & low-stock alerts.** Add `Product.ReorderThreshold` (`int?`) and
      `Product.ReorderQuantity` (`int?`, informational — how much to reorder when triggered).
      `GET .../inventory/low-stock` lists every product where `StockQuantity <= ReorderThreshold`.
      No email/SMS push yet — that's Roadmap §9.10's job once it exists; this just makes the signal
      queryable.
- [ ] **9.15c — Manual stock adjustment endpoint.** `POST .../products/{productId}/stock-adjustments`
      (BusinessAdmin/BusinessStaff) for physical recounts/damage write-offs, going through
      `RecordMovementAsync` from 9.15a so corrections are logged like everything else, replacing the
      current silent overwrite in `ProductService.UpdateAsync`.
- [ ] **9.15d — Inventory valuation.** `GET .../inventory/valuation` — `Σ(StockQuantity × Price)`,
      overall and per category. Feeds the "inventory asset" line in the Accounting module's balance
      sheet (§9.16c) — do this after 9.15a–c, before 9.16c.

### 9.16 Accounting module — sales ledger & balance sheet (added 2026-08-15)
Nothing today aggregates revenue, refunds, or costs anywhere — an `Order`'s money story lives only
on that one document. Deliberately scoped as **single-entry, cash-basis bookkeeping**, not full
double-entry/GAAP — that's a explicit simplification worth stating up front, same spirit as §7's
other cuts. Four sessions, roughly in order:
- [ ] **9.16a — Sales ledger.** New `LedgerEntry` entity (Business/Tenant-scoped): `Type`
      (`Revenue`/`Refund`), `Amount`, `Currency`, `ReferenceOrderId`, `OccurredAt`. Written
      automatically when `OrderService` transitions an order's `Status` to `Delivered`/`Completed`
      (revenue) or processes a cancellation/refund after payment (refund) — deliberately *not* at
      order creation, so a never-fulfilled order never inflates revenue.
- [ ] **9.16b — Manual expense tracking.** New `Expense` entity (`Category` free-text, `Amount`,
      `Note`, `IncurredAt`, `CreatedByUserId`) with full BackOffice CRUD
      (`GET/POST/PUT/DELETE .../expenses`) for costs that don't come from orders (rent, ads, wages).
- [ ] **9.16c — P&L and balance sheet reports.** `GET .../accounting/profit-and-loss?from&to` (sum of
      9.16a revenue/refunds minus 9.16b expenses over a window) and `GET .../accounting/balance-sheet`
      (running cash position plus the inventory valuation from §9.15d as the asset line). Both are
      computed on the fly from existing documents, not materialized — fine at current scale.
- [ ] **9.16d — Delivery agent balance as a ledger line.** `DeliveryAgentProfile.Balance` already
      exists but nothing writes to it (§7, cross-referenced at §9.7) — once §9.7 implements earnings
      logic, feed agent payouts into the P&L as an expense line here rather than building a second,
      disconnected number.

---

## 10. Frontend projects

Three companion blueprint docs exist, one per planned frontend, each **self-contained** (safe
to move into that project's own repo and hand to a fresh session with no other context):

| Doc | App | Stack | Audience |
|---|---|---|---|
| [`docs/SUPEROFFICE_FRONTEND_BLUEPRINT.md`](docs/SUPEROFFICE_FRONTEND_BLUEPRINT.md) | SuperOffice | React (Vite SPA) | `TenantOwner` — cross-business control panel |
| [`docs/BACKOFFICE_FRONTEND_BLUEPRINT.md`](docs/BACKOFFICE_FRONTEND_BLUEPRINT.md) | BackOffice | React (Vite SPA) | `BusinessAdmin`/`BusinessStaff`/`DeliveryAgent` — one Business's day-to-day ops |
| [`docs/ANTIVALY_SHOP_BLUEPRINT.md`](docs/ANTIVALY_SHOP_BLUEPRINT.md) | Antivaly (pilot shop) | Next.js | Public Landing Page + Shop + `Customer` account, for the first real Business onboarded onto Vastora |

Each doc carries: the exact API contracts it needs (method, path, request/response JSON
shapes, auth requirements — pulled directly from the current DTOs, not paraphrased), a
recommended page/screen breakdown, an auth/token-storage strategy, and — for SuperOffice and
BackOffice specifically, since those two ship to real clients — a deployment config file spec
(§ "Environment configuration" in each) so shipping either app to a new business is "fill in
the config, deploy" rather than a code change. If the API surface changes (new endpoint, DTO
field renamed, enum value added), update the relevant blueprint doc(s) in the same session —
they will drift out of sync with the code otherwise, same as this file.

---

## 11. Progress Log

Newest entry first. Keep entries short — what happened and why, not a diff.

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
