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
| `Coupon` | Business | Percentage or fixed discount, usage cap, validity window (`ExpiresAt` nullable since §9.43 — null means it never expires). `Visibility` (§9.43): `Public`/`Hidden`. `IsValidNow` computed property. |
| `Cart` | Business + Customer | One live cart per customer per business, embedded `CartItem` list, optional coupon code, gift-card codes and a `UseStoreCredit` toggle (§9.43) that now actually feed the priced preview. `FulfillmentMethod` (§9.44, default `Delivery`) — `Pickup`/`Digital` drop the delivery fee and shipping options from the preview entirely. |
| `Order` | Business | Embedded `OrderItem` snapshot (price/name captured at checkout, immune to later product edits), `StatusHistory` + `PaymentStatusHistory` audit trails, `PaymentStatus` separate from fulfillment `Status` (transition rules enforced — §9.7). |
| `DeliveryAgentProfile` | Business | Operational stats for a `DeliveryAgent` user — status, balance (credited on delivery — §9.7), level, completed count. |
| `StockMovement` | Business | One row per `Product.StockQuantity` change — Sale/Restock/Return/Adjustment/DamageWriteOff, signed `QuantityDelta` (§9.15a). |
| `LedgerEntry` | Business | Revenue/Refund/DeliveryPayout, written only by `OrderService` on Delivered/Refunded transitions and agent payout (§9.16a) — no direct-write endpoint. |
| `Expense` | Business | Manually entered cost not tied to an Order (rent, ads, wages) — full BackOffice CRUD (§9.16b). |

**Added by §9B (2026-08-16).** Every one of these implements `ITenantScoped`/`IBusinessScoped`
where relevant and inherits the soft-delete fields §9.35 added to `BaseEntity`.

| Entity | Scoped to | Purpose |
|---|---|---|
| `Review` | Business | Customer rating of a Product (§9.25). Server-verified purchase, moderation status, merchant reply. Aggregated onto `Product.AverageRating`/`ReviewCount`. |
| `WishlistItem` | Business + Customer | One saved product per row — not an embedded list — so back-in-stock alerts can query "who wants this?" without scanning users (§9.26). |
| `ReturnRequest` | Business | RMA aggregate (§9.21): partial line/quantity returns, own lifecycle, restock on `Received`, refund settlement on `Refunded`. |
| `Promotion` | Business | The general discount rule `Coupon` can't express (§9.23) — automatic/coded, BOGO, free shipping, scoped, group-targeted, per-customer capped. `Coupon` is untouched and still works. `Visibility` (§9.43): `Public`/`Hidden`, same as `Coupon`. |
| `CustomerGroup` | Business | Named customer segment with a blanket group discount (§9.23). Membership lives here; `AppUser.CustomerGroupIds` is only a cache. Also the segment a §9.43 targeted discount email can address. |
| `GiftCard` | Business | Hashed code shown once, drawn down at checkout. A **liability** until redeemed, not revenue (§9.24). |
| `StoreCreditEntry` | Business + Customer | Append-only credit ledger; the balance is always the sum of entries, never a mutable field (§9.24). `ExpiresAt` (§9.43, nullable) — only meaningful on a positive/credit entry; a refund settlement leaves it null (never expires), a promotional grant can set it. Computed live via `CountsTowardBalance`, no sweep. |
| `ShippingZone` | Business | Destination bands with subtotal/weight rate tables, replacing the flat `DefaultDeliveryFee` (§9.20). |
| `ContentBlock` | Business | Polymorphic storefront content: banners, pages (Terms/Privacy), nav items, articles (§9.30). |
| `EmailVerificationToken` | User | Hashed, expiring, single-use — same shape as `PasswordResetToken` (§9.34). |
| `AuditLogEntry` | Business | Who changed what, written centrally by `AuditLogFilter` for every mutating request (§9.35). |
| `IdempotencyRecord` | — | Caller + key → stored response, so a retried checkout replays instead of re-charging (§9.17). TTL-expired by Mongo. |
| `WebhookSubscription` / `WebhookDelivery` | Tenant | Tenant-registered HTTPS endpoints, HMAC-signed payloads, per-attempt audit rows (§9.39). |
| `ApiKey` | Tenant | Server-to-server credential: public key id + hashed secret, scopes, expiry, revocation (§9.39). |

**Changed existing entities.** `Product` gained `CostPrice` (the field that unblocked §9.31),
weight/dimensions, brand, barcode, SEO meta, publish window, featured flag and tax class.
`Order` gained a currency snapshot, tax fields, cost-per-line snapshots, billing address,
contact snapshot, guest fields, fulfillment method, carrier tracking, invoice number and
discount/gift-card breakdowns. `Cart` gained a guest token, variant-aware items and promotion
codes. `Business` gained `Tax`, `Invoicing`, return-window and reviews/guest-checkout switches.
`AppUser` gained `EmailVerifiedAt`, `NotificationPreferences` and `AnonymizedAt`.

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

### Endpoints added by §9B — 2026-08-16

128 routes total, 55 of them new. **Two changes affect existing callers:** every list endpoint
now returns a `PagedResult<T>` envelope (`{ items, page, pageSize, totalCount, totalPages,
hasNextPage, hasPreviousPage }`) instead of a bare array, and the public catalog is filtered
via a `CatalogQuery` querystring rather than two loose parameters.

**Auth (§9.34, §9.36)**
- `POST /api/auth/verify-email` — public; the token identifies the account, same as password reset.
- `POST /api/auth/resend-verification` — any authenticated user.
- `POST /api/auth/unsubscribe/{token}` — public; one-click marketing opt-out, no login required.

**Shop — cart (§9.22, §9.23, §9.27)**
- The whole cart controller is now `[AllowAnonymous]`. A signed-in Customer is scoped by JWT; a
  guest sends `X-Cart-Token` (minted server-side on first write, returned as `guestToken`) plus
  `?businessId=`. An authenticated identity always wins over any token that's also present.
- `POST/DELETE /api/shop/cart/coupon` — `DELETE` added 2026-08-18 (§9.45); every sibling code type (promotions, gift cards) already had a matching removal endpoint, coupon never did — a shopper could apply one but only take it off by clearing the whole cart.
- `POST/DELETE /api/shop/cart/promotions[/{code}]` — stackable promotion codes, separate from the single legacy coupon.
- `POST/DELETE /api/shop/cart/gift-cards[/{code}]` — apply/remove a gift card code on the cart itself (§9.43; previously only settable at final checkout, priced nowhere before that).
- `PUT /api/shop/cart/store-credit` — Customer only; opts the cart in/out of spending store credit, mirroring `CheckoutRequest.UseStoreCredit` (§9.43).
- `PUT /api/shop/cart/fulfillment-method` — Guest and Customer; sets `Delivery`/`Pickup`/`ExternalCourier`/`Digital` on the cart so its preview reflects the right delivery fee before checkout (§9.44).
- `GET /api/shop/cart/available-offers` — Public coupon/promotion codes worth showing this shopper now; excludes `Hidden` ones (§9.43).
- `POST /api/shop/cart/merge?guestToken=` — Customer only; folds the anonymous cart in on login.
- Cart responses now carry `discounts`, `discountTotal`, `estimatedTotal`, `itemCount`, `currency`, (§9.43) `giftCardCodes`, `giftCardTotal`, `useStoreCredit`, `storeCreditApplied`, `amountDue`, and (§9.44) `fulfillmentMethod`, `deliveryFee`, `shippingMethodName`, `shippingOptions`.

**Shop — checkout & orders (§9.17, §9.21, §9.27)**
- `POST /api/shop/orders/checkout` — now `[AllowAnonymous]`; honours an optional `Idempotency-Key` header.
- `POST /api/shop/orders/preview` — prices the cart with nothing committed: no stock moves, no coupon burnt.
- `GET /api/shop/orders/lookup?businessId&orderNumber&email` — public; guest order tracking.
- `POST/GET /api/shop/orders/returns`, `POST .../returns/{id}/cancel` — Customer-initiated RMAs.

**Shop — account (§9.24, §9.25, §9.26, §9.37)**
- `GET/POST/DELETE /api/shop/account/wishlist[/{productId}]`
- `POST /api/shop/account/reviews`
- `GET /api/shop/account/store-credit`, `GET .../gift-cards/{code}`
- `GET /api/shop/account/data-export`, `PUT .../notification-preferences`, `DELETE /api/shop/account`

**Shop — public catalog (§9.25, §9.26, §9.29, §9.30)**
- `GET /api/shop/{slug}/products` — now takes `CatalogQuery` (`categoryId`, `search`, `minPrice`,
  `maxPrice`, `brand`, `tags`, `inStockOnly`, `minRating`, `featuredOnly`, `sort`, `page`, `pageSize`)
  and returns a paged envelope. **`costPrice`/`unitMargin` are stripped from this projection.**
- `GET .../products/facets` — filter values and counts over the same filter as the listing.
- `GET .../products/{id}/reviews`, `.../reviews/summary`, `POST .../reviews/{id}/helpful`
- `GET .../products/{id}/also-bought`, `.../related`
- `GET .../banners`, `.../menu`, `.../pages`, `.../pages/{slug}`

**BackOffice — orders (§9.20, §9.33)**
- `GET .../orders` — now takes `OrderQuery` (`status`, `paymentStatus`, `search`, `from`, `to`, `page`, `pageSize`).
- `PATCH .../orders/{id}/shipment` — external courier tracking; also advances to `OutForDelivery`.
- `PATCH .../orders/{id}/internal-note`
- `GET .../orders/{id}/invoice` — assigns a gapless sequential number on first call.

**BackOffice — returns & reviews (§9.21, §9.25)**
- `GET .../returns`, `GET .../returns/{id}`, `POST .../returns/{id}/decision`, `.../received`,
  `.../refund` — refund is Admin-tier, the rest Staff-permitted.
- `GET .../reviews`, `PATCH .../reviews/{id}/status`, `POST .../reviews/{id}/reply`, `DELETE .../reviews/{id}`.

**BackOffice — merchandising, Admin-tier only (§9.20, §9.23, §9.24, §9.30, §9.43)**
- `.../promotions`, `.../customer-groups` (+ `/members`), `.../gift-cards`,
  `.../customers/{id}/store-credit`, `.../shipping-zones`, `.../content` — full CRUD on each.
- `POST .../discount-emails` — emails a coupon/promotion code to named customers and/or a
  customer group (§9.43); the delivery mechanism a `Hidden`-visibility code needs, since it never
  appears in the storefront's available-offers listing on its own.

**BackOffice — catalog, analytics, audit (§9.28, §9.32, §9.35)**
- `POST .../products/import`, `GET .../products/export` — CSV, upsert keyed on SKU, Admin-tier.
- `GET .../analytics/dashboard?from&to` — the per-business dashboard §9.8 never provided.
- `GET .../audit-log` — Admin-tier; filterable by user, resource and date.

**SuperOffice — integrations, TenantOwner only (§9.39)**
- `GET/POST/DELETE /api/integrations/webhooks[/{id}]`, `GET .../webhooks/events`, `GET .../webhooks/{id}/deliveries`
- `GET/POST/DELETE /api/integrations/api-keys[/{id}]`

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

**Updated 2026-08-16 — three of the items above are now stale, and this is what replaced them:**

- **Product variants are no longer catalog-only.** §9.22 threaded `VariantId` through
  Cart → Order → StockMovement, made variant stock atomic, and made `PriceOverride` count.
- **Integration tests are still absent**, unchanged and still blocked on Docker. The unit suite
  grew from 37 to 78 tests, including regression coverage for every correctness defect §9B found.
- **CORS still defaults open**, unchanged. §9.40 did tighten the credential endpoints to
  10 requests/minute against the global 100.

**Still exactly as described above:** no payment gateway, no self-serve tenant upgrade flow, no
real email/SMS provider, local-disk file storage, and single-entry cash-basis accounting — though
§9.31 fixed that model's two actual *errors* (inventory valued at retail, no COGS) and gave it a
liabilities side, so it is now simple rather than wrong.

The list above is only what the *foundation* roadmap knowingly cut. The full audit against a
production e-commerce platform is §9.17–§9.40, and §9.40 consolidates the remaining
infrastructure items into one pre-launch checklist.

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
infrastructure (Docker, for integration tests). Read each section's own "not done" note for the
specifics rather than treating this paragraph as the authority; it will go stale faster than
they will.

**§9.1–§9.16 is the foundation roadmap. §9.17–§9.40 (§9B, audited and implemented 2026-08-16)
is the commerce-completeness roadmap — 55 of its 77 items are done.** Between them the backend
now covers what a production e-commerce platform is expected to do: checkout that can't oversell
or double-charge, tax, shipping zones, returns and partial refunds, variant-aware carts, a real
promotions engine, gift cards and store credit, reviews, wishlists, guest checkout, faceted
search, storefront content, correct COGS-based accounting, per-business analytics, invoices,
email verification, audit logging, lifecycle notifications, data-rights endpoints and tenant
webhooks.

The 22 open items are listed with their reasons in §9B's intro. The short version: most need a
vendor or product decision rather than code, integration tests are still blocked on Docker, and
the rest were scoped down on purpose.

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
- [x] **Re-parenting, added 2026-08-17.** `UpdateCategoryRequest` now carries `ParentCategoryId`,
      so a category can be moved to a different parent or promoted back to top-level after
      creation — it wasn't possible before (`Update` only ever touched name/description/image/
      sort/active). `CategoryService.UpdateAsync` rejects a category as its own parent and walks
      the new parent's ancestor chain to reject its own descendants too, either of which would
      make `GetTreeAsync`'s recursive build loop forever. Covered by three tests in
      `CategoryServiceTests`.
- [x] **Subcategory-inclusive catalog filtering, added 2026-08-18.** The gap flagged above is
      closed: `?categoryId=` on the public catalog (`GET .../products`, `.../products/facets`)
      now matches the requested category **and every descendant**, not an exact id match —
      browsing "Electronics" surfaces "Phones" products without the shopper having to know
      "Phones" exists. `ProductService.ResolveCategoryAndDescendantIdsAsync` does the same
      flat-collection tree walk `CategoryService.GetTreeAsync` does, just flattened to an id set
      (`HashSet<string>`) instead of a nested tree, then the existing `p.CategoryId == categoryId`
      predicate became `categoryIds.Contains(p.CategoryId)`. No new endpoint: because facets are
      computed over "the same filter as the listing" (§9.29's existing promise), `GET
      .../products/facets`'s `categories` breakdown, once given a parent's id, now naturally
      returns per-subcategory counts within that parent — that *is* the "subcategory filter"
      data a category page needs, with no separate mechanism invented for it. Picking a child
      category from that breakdown narrows back down to an exact match, same as before.
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
- [x] **Real SMTP transport, added since this section was written.** `SmtpNotificationService`
      (MailKit) sends for real whenever `Smtp:Host` is configured; `LoggingNotificationService`
      stays the default (blank `Smtp:Host`) so local dev/demo keeps working exactly as above.
      Selected in `DependencyInjection.AddInfrastructure` by presence of `Smtp:Host` — no caller
      changed. Still no SendGrid/SES/Twilio integration specifically; MailKit talks to any SMTP
      account, which is the more general fix.
- [x] **Branded HTML templates, added 2026-08-17.** Every email in the system — verification,
      password reset, order confirmation/status-change/shipped, return decision, refund issued,
      abandoned cart, back-in-stock, merchant low-stock, review request — now renders as a real
      HTML email (plus a plain-text fallback in the same `multipart/alternative` message) instead
      of a bare interpolated string. One shared layout (`EmailTemplates.Layout`,
      `Vastora.Application/Notifications/EmailTemplates.cs`) carries the `Business`'s
      `LogoUrl`/`Name`/`ThemeColor` into every customer-facing message, falling back to generic
      Vastora branding when there's no single Business to brand as (a BackOffice/Platform
      password reset). `NotificationMessage` gained an optional `HtmlBody`; `INotificationService`
      itself didn't change shape. Password-reset and email-verification now build a real
      `{PublicBaseUrl}/...?token=...` link instead of handing back a bare token — `PublicBaseUrl`
      existed for exactly this and was previously unused. Marketing-adjacent templates (abandoned
      cart, back-in-stock, review request) carry an unsubscribe link built from the recipient's
      `UnsubscribeToken`. All dynamic content is HTML-encoded before interpolation — several of
      these bodies embed staff-entered or customer-entered free text (a return decision note, a
      product name), so this is the templates' own XSS guard, independent of the SMTP transport.
      **Still open, deliberately not built this pass:** no gift-card-issuance or invoice-delivery
      email exists (§9.24/§9.33 have the data but never call `INotificationService`), and
      `AssignDeliveryAgentAsync`/`UpdatePaymentStatusAsync`/return `MarkReceivedAsync`/`CancelAsync`
      still send nothing on their own transitions.

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

### Commerce-completeness audit — 2026-08-16 (§9B, implemented 2026-08-16)

§9.1–§9.16 built the platform *skeleton*: multi-tenancy, catalog, cart, orders, inventory,
accounting, auth. This section is the result of auditing that skeleton against what a
production e-commerce platform is actually expected to do — both the correctness gaps found by
reading the code, and the feature primitives that every mature commerce API (Medusa, Saleor,
commercetools, Shopify) treats as core and Vastora currently has no representation for at all.

**§9B was audited and then implemented in the same day (2026-08-16).** Each section below keeps
its original finding — the evidence and the *why*, written before any code — and now carries a
bold status line stating exactly what landed and what did not. Read the finding for the
reasoning and the status line for the truth; where they disagree, the status line is newer.

**55 of 77 items are done; 22 remain open and stay unchecked.** What's left clusters into three
honest categories, and none of it is an oversight:

- **Needs a product or vendor decision, not code.** Cloud storage provider, image CDN, payment
  gateway (§9.6), real email/SMS provider (§9.10), deployment target, secret store, APM vendor.
  The seams exist in every case; the choice does not.
- **Needs infrastructure this environment doesn't have.** Integration tests are still blocked on
  Docker, exactly as §9.12 recorded.
- **Deliberately scoped down.** Atlas Search (§9.29 still regex-scans), per-product price lists
  (§9.23 ships group-level discounts instead), partial shipment (§9.20 needs per-item fulfillment
  state), PDF rendering (§9.33 ships the data, the frontends render it), loyalty points, product
  Q&A, multi-language content, and custom-domain routing.

**One item is a genuine half-step and worth calling out:** §9.39's `IApiKeyService` issues and
validates keys and is unit-tested, but no authentication *handler* consumes them yet — keys can
be created and verified, not yet used to authenticate a live request.

### 9.17 Checkout integrity — transactions, oversell, idempotency

**done (2026-08-16).** All four sub-items landed. Lines are validated before any stock moves; each deduction is an atomic guarded `$inc` via the new `IProductStockStore`, so overselling is refused by the database rather than by a C# read-then-write; a mid-flight failure compensates every deduction already applied; `Idempotency-Key` is honoured on checkout via `[Idempotent]`; and coupon/promotion usage now increments through `TryIncrementBelowAsync`. **Not** a MongoDB transaction — compensating rollback instead, because sessions would have to thread through every repository call; the trade-off is written up in `CompensateStockAsync`'s own comment. Smoke-tested live: a two-line cart whose second line was short left the first line's stock at exactly 50, with no order written.
The highest-severity finding in this audit. `OrderService.CheckoutAsync`
(`src/Vastora.Application/Orders/OrderService.cs`) has three distinct integrity problems, all
in the same ~40 lines:
- [x] **Stock is deducted inside the item loop, before the order exists.** Each iteration calls
      `inventoryService.RecordMovementAsync(...)` immediately. If item 3 of 5 throws
      (`NotFoundException` for a deleted product, `ConflictException` for insufficient stock),
      items 1 and 2 have already been decremented and had `StockMovement` rows written — and
      nothing rolls them back. The customer gets a 404/409, the merchant silently loses stock.
      Same exposure if the coupon call or `orders.AddAsync` fails after the loop completes.
      Fix shape: validate every line item's availability *first*, then write. A full fix wants a
      MongoDB multi-document transaction (`IClientSessionHandle`) — Atlas is a replica set, so
      this is available today; `IMongoRepository<T>` has no session parameter, so the interface
      needs one threaded through.
- [x] **Oversell race.** `if (product.TrackInventory && product.StockQuantity < cartItem.Quantity)`
      is a read, and the decrement is a separate later write. Two concurrent checkouts for the
      last unit both pass the check. Needs a conditional atomic update
      (`FindOneAndUpdate` with `StockQuantity >= qty` in the filter and `$inc` in the update),
      which `IMongoRepository<T>` cannot currently express — it only has whole-document
      `UpdateAsync`. Alternatively a reservation model (hold stock at cart, release on
      expiry), which is the more standard commerce answer but a much larger change.
- [x] **No idempotency.** `POST /api/shop/orders/checkout` has no idempotency key. A double-tap
      on a flaky connection places two real orders and deducts stock twice. Standard fix: an
      `Idempotency-Key` header, a short-lived key→response collection, replay the stored
      response on repeat. Worth doing before a payment gateway (§9.6) makes double-submission
      cost real money.
- [x] **Coupon usage is not atomic either.** `couponService.RegisterUsageAsync` increments a
      counter with the same read-then-write shape, so a usage-capped coupon can be redeemed
      past its cap under concurrency.

### 9.18 Pagination, filtering & sorting on every list endpoint

**done (2026-08-16).** `IMongoRepository.FindPagedAsync` + `PagedResult<T>`/`PageRequest`, with skip/limit/sort/count evaluated by MongoDB. Every list endpoint returns the envelope; `OrderQuery` adds server-side status/payment/date/search filtering. `DatabaseInitializer` now creates the compound query-path indexes these reads need, separately from the pre-existing uniqueness constraints. **Breaking:** list responses are now `{items, page, pageSize, totalCount, ...}` rather than a bare array — all three frontend blueprints were updated in the same session.
- [x] `IMongoRepository<T>` (`Vastora.Application/Common/Interfaces/`) exposes `GetAllAsync`
      and `FindAsync` and **nothing else** — no skip, no limit, no sort, no total count. Every
      list endpoint in the API consequently returns the entire matching collection: orders,
      products, customers, stock movements, ledger entries. A business with 50k orders gets a
      50k-document response, and `GetForBusinessAsync` then sorts it *in memory* with
      `.OrderByDescending(o => o.PlacedAt)` after the driver has already materialised the lot.
      This is fine at the current smoke-test scale and fails hard at real scale.
      Already flagged from the client side in `docs/SUPEROFFICE_FRONTEND_BLUEPRINT.md` — the
      frontend docs' pagination note and this item are the same gap.
- [x] Add skip/take/sort/total to the repository, a shared `PagedResult<T>` envelope, and a
      consistent `?page=&pageSize=&sort=` convention across every list route. Changing the
      response shape from a bare array to an envelope is a **breaking API change** — do it
      before the three frontends are written, not after, and update all three docs in the same
      session (per the rule at the top of this file).
- [x] Add the compound indexes the paged queries will need (`(BusinessId, PlacedAt)` on Order,
      `(BusinessId, Status)` on Product, ...). `DatabaseInitializer` today creates only unique
      constraint indexes, not query-path indexes.

### 9.19 Tax

**done (2026-08-16), single-rate.** `Business.Tax` (`TaxSettings`) + `Product.TaxClass`, `ITaxService`, and `Order.TaxAmount`/`TaxRatePercent`/`PricesIncludeTax` snapshotted at checkout. Tax is computed on the **discounted** base and, for tax-inclusive businesses, *extracted* from the price rather than added on top. `TaxCollected` is its own ledger type, so it lands as a liability rather than revenue. Verified live: a 200 basket with a 20 discount and a 10% rate produced 18.00, not 20.00. Still deliberately **not** a jurisdiction engine — no per-region nexus rules, no Avalara/TaxJar; `ITaxService` is the seam for that.
- [x] There is no tax anywhere in the system. `Order` has `Subtotal`, `DiscountAmount`,
      `DeliveryFee`, `Total` and no `TaxAmount`; `Business` has no tax registration number, no
      tax rate, no "prices include tax" flag. For most jurisdictions this makes the platform
      unusable for a legally operating seller, and it makes §9.33's invoices impossible to
      issue correctly.
- [x] Minimum viable shape: a per-Business tax rate + tax-inclusive/exclusive pricing flag,
      `Order.TaxAmount` computed at checkout and snapshotted like every other money field, and
      tax as its own line in §9.16's P&L (tax collected is a liability, not revenue — the
      current single-entry model has no liabilities at all, see §9.31).
- [x] Anything beyond that (per-region rates, product tax classes, VAT vs. sales tax, an
      Avalara/TaxJar provider behind an `ITaxService`) is a real project. Scope it when a tenant
      actually needs it; do not build a US sales-tax nexus engine on spec.

### 9.20 Shipping & fulfillment depth

**mostly done (2026-08-16).** `ShippingZone`/`ShippingRate` with country+region matching (most specific wins), subtotal and weight bands, free-shipping thresholds expressed as a zero-priced band, and multiple selectable methods at checkout. `Product.WeightKg`/dimensions added to feed it. External-courier tracking is now first-class (`PATCH .../orders/{id}/shipment`, which also advances the order to `OutForDelivery`), and `FulfillmentMethod` covers Pickup/ExternalCourier/Digital — closing §9.14's deferred pickup item. **Still not done:** partial shipment / split fulfillment, which needs per-item fulfillment state rather than one `Order.Status`.
`Business.DefaultDeliveryFee` is one flat decimal, and §9.7 already noted zone/distance logic
was out of scope. The full list of what a real seller expects and cannot express today:
- [x] **Shipping zones / rate tables** — fee by destination, order value, or weight. Requires
      product weight/dimensions, which `Product` does not have (§9.28).
- [x] **Multiple shipping methods at checkout** — standard vs. express, customer-selectable.
      `CheckoutRequest` takes a raw `DeliveryFee?` and no method identifier.
- [x] **Free-shipping thresholds** — currently only expressible as a coupon, badly.
- [x] **Third-party courier tracking.** `Order` has `DeliveryAgentUserId` and no carrier name,
      tracking number, or tracking URL — so a Business with `DeliveryModuleEnabled = false`
      (§9.14) that ships via an external courier has nowhere to record the shipment at all.
- [ ] **Partial shipment / split fulfillment** — one `Order.Status` for the whole order means a
      partially-shipped order cannot be represented.
- [x] **Pickup as a first-class fulfillment type** — already flagged as deferred at the end of
      §9.14; it belongs here.

### 9.21 Returns, exchanges & partial refunds

**done (2026-08-16).** Full RMA aggregate: `ReturnRequest` with its own lifecycle (Requested → Approved → Received → Refunded), partial line-and-quantity returns, a return window enforced from the Business's own `ReturnWindowDays`, staff approval that can settle for less than requested, and refund-to-original / store-credit resolutions. **The restock bug is fixed twice over:** returns restock on `→ Received` (not on approval, and not on refund), and the plain `→ Refunded` order transition — which previously wrote a ledger entry and left inventory untouched — now restocks too. Damaged returns write a `DamageWriteOff` note instead of restocking. Verified live: 3 bought, 1 returned, stock 47 → 48 only after Received, order stayed `Delivered` with `refundedAmount: 50`.
- [x] `OrderStatus.Refunded` is the entire returns story: whole-order, all-or-nothing, no
      customer-initiated path, no reason capture, no approval step, and — importantly — **no
      restock**. Compare `CancelAsync`, which does write `Return` stock movements; the
      `→ Refunded` transition writes a `LedgerEntry` and leaves inventory untouched. A refunded
      item silently vanishes from stock.
- [x] No partial refund: no amount field on the refund path, so refunding one line of a
      five-line order is not representable, and §9.16a's `Refund` ledger entry can only ever be
      for the full order total.
- [x] No RMA entity, no return window policy, no exchange flow. Every mature commerce API
      models return-vs-exchange as first-class — this is the single largest missing customer-
      facing workflow in the system.

### 9.22 Variant-aware Cart & Order

**done (2026-08-16).** `VariantId` threads through `CartItem` → `OrderItem` → `StockMovement`. Variant stock is decremented atomically by its own positional array update, `PriceOverride` is respected by `PricingService`, and a product that *has* variants can no longer be added to a cart without choosing one — previously the storefront could display variants it had no way to sell.
- [x] Promoting the boundary §9.5 deliberately drew into its own roadmap item, because it is
      the one place where a shipped feature is actively misleading: `Product.Variants` exists,
      is BackOffice-manageable, and is returned to the storefront — but `CartItem` and
      `OrderItem` carry a bare `ProductId`, `ProductVariant.StockQuantity` is decremented by
      nothing, and `EffectivePrice` ignores `PriceOverride`. A shop selling "Red / Large"
      cannot actually sell "Red / Large" today; the storefront can display variants it has no
      way to let a customer buy.
- [x] Needs `VariantId` threaded through `CartItem` → `OrderItem` → `StockMovement`, variant-
      level stock as the authority when variants exist, and `PriceOverride` respected in
      pricing. Touches checkout, so land it *after* §9.17 rather than fighting the same code
      twice.

### 9.23 Promotions engine & customer segments

**done (2026-08-16).** New `Promotion` aggregate alongside (not replacing) `Coupon`, so every existing code keeps working through the path it always used. Covers automatic no-code discounts, BOGO settled against the cheapest qualifying units, free shipping, product/category scoping, customer-group targeting, first-order-only, per-customer caps, priority and stackability. `CustomerGroup` adds segments with a blanket group discount — the wholesale-tier case. **Still open:** a full per-product price list; the group-level percentage is the deliberate scope-down.
- [x] `Coupon` supports one percentage-or-fixed code with a usage cap and a validity window,
      and `Cart` holds exactly one `CouponCode`. Not expressible: buy-X-get-Y, free shipping,
      tiered/threshold discounts, category- or product-scoped discounts, stacking rules,
      first-order-only, per-customer usage limits (the cap is global), scheduled campaigns, or
      automatic discounts with no code at all.
- [x] **No customer groups / segments.** Every mature platform (Medusa's Customer Groups and
      Price Lists, Shopify's segments) uses them for B2B pricing, wholesale tiers, and targeted
      promotions. Vastora's `AppUser` has a `Role` and nothing else to segment on.
- [ ] **No price lists** — no way to give one customer group different prices, and no scheduled
      price changes beyond the single `DiscountPercent`/`DiscountExpiresAt` pair on `Product`.

### 9.24 Gift cards, store credit & loyalty

**done (2026-08-16), loyalty excluded.** `GiftCard` (hashed code, shown once, balance-checked and redeemed at checkout) and an append-only `StoreCreditEntry` ledger whose balance is always the sum of its entries. Issuing a gift card books `GiftCardIssued`, and the outstanding float appears as a **liability** on the balance sheet — so selling one no longer reads as pure profit. Store credit is also the settlement route for §9.21 returns. **Not built:** points-based loyalty, which is a product decision about earn/burn rates rather than a missing mechanism.
- [x] None of the three exist. Gift cards in particular are both a payment instrument and a
      liability (see §9.31 — the accounting module has no liability concept, so selling a gift
      card today would book as pure revenue and overstate profit).
- [x] Store credit is also the natural settlement mechanism for §9.21's refunds, so the two
      are worth scoping together.

### 9.25 Reviews, ratings & Q&A

**done (2026-08-16).** `Review` with server-verified purchase (checked against real `Delivered` order history, never trusted from the client), one review per customer per product enforced by a unique index as well as a service check, moderation queue, merchant replies, helpful counts, and a denormalised `AverageRating`/`ReviewCount` on `Product` for sorting. Held `Pending` by default unless the Business opts into `AutoPublishReviews`. Product Q&A was **not** built — same shape, no demand yet.
- [x] No review entity, no rating aggregate on `Product`, no moderation queue, no
      verified-purchase check. Already documented as an absence in
      `docs/ANTIVALY_SHOP_BLUEPRINT.md` ("don't design a rating widget around data that doesn't
      exist") — this item is the backend side of that note.
- [x] Social proof is one of the highest-conversion-impact features in e-commerce and one of
      the cheapest to build here: a `Review` entity scoped to Business + Product + Customer, a
      denormalised `AverageRating`/`ReviewCount` on `Product` for sorting, and Admin-tier
      moderation reusing the §9.3 permission split.
- [ ] Product Q&A is the same shape and can share the entity if scoped down.

### 9.26 Wishlist, recently viewed & recommendations

**done (2026-08-16).** `WishlistItem` as a row per saved product (not an embedded list), specifically so §9.36's back-in-stock sweep can ask "who wants this product?" without scanning every user. `also-bought` is computed from real order co-occurrence; `related` falls back to same-category when there's no order history. Recently-viewed was **not** persisted server-side — it is client state, and storing it would add a write on every product view for no capability the client doesn't already have.
- [x] No wishlist/favorites endpoint (also already flagged in the Shop blueprint), no
      recently-viewed, no related/cross-sell products, no "customers also bought". The
      `Order` collection already contains everything a first-cut co-purchase recommendation
      needs — this is a query, not a data model change.
- [x] Wishlist is the prerequisite for back-in-stock notifications (§9.36).

### 9.27 Guest checkout & cart merge

**done (2026-08-16).** Anonymous carts keyed on an `X-Cart-Token` the server mints on first write; guest checkout with an email-only contact snapshot; `POST /api/shop/cart/merge` folding the guest cart into the customer's on login (quantities summed, not replaced); and `GET /api/shop/orders/lookup` by order number + email, since a guest has no account to list orders under. Per-Business `GuestCheckoutEnabled` lets a seller still require accounts.
- [x] Checkout requires a `Customer` JWT (`CheckoutAsync` takes a non-null `customerUserId`;
      `Cart` is keyed on `BusinessId + CustomerUserId`). Forced registration before purchase is
      a well-documented conversion killer, and the Shop blueprint already calls it out.
- [x] Needs an anonymous cart identity (cart token cookie), an email-only order path, and a
      **cart merge on login** — today a customer who fills a cart then logs in has no defined
      merge behaviour because the anonymous cart cannot exist in the first place.
- [x] Guest orders also need a lookup-by-email+order-number route, since there is no account to
      list them under.

### 9.28 Product data completeness & bulk import/export

**done (2026-08-16).** `CostPrice` (the one that unblocked §9.31), weight and dimensions, brand, barcode, SEO meta fields, publish/unpublish window, featured flag, sort weight and tax class — plus CSV bulk import (upsert keyed on SKU) and export. `IsPubliclyVisibleNow` means an Active product genuinely can be scheduled. **Not done:** `sitemap.xml` and structured-data output, which belong to the storefront app rather than the API.
`Product` is missing fields that downstream features need before they can be built at all:
- [x] **`CostPrice`** — blocks §9.31 entirely (COGS, gross margin, inventory-at-cost). The
      single highest-value field on this list.
- [x] **Weight & dimensions** — blocks §9.20's weight-based shipping rates.
- [x] **Brand/manufacturer, barcode/GTIN/UPC** — needed for marketplace feeds (Google Shopping,
      Meta catalogs) and for any real warehouse workflow.
- [ ] **SEO fields** (meta title/description, canonical URL) and a storefront `sitemap.xml` /
      structured-data feed. The platform sells storefronts; storefronts that cannot be indexed
      are worth measurably less.
- [x] **Publish window / featured flag / sort weight** — `ProductStatus` is Draft/Active only,
      so "goes live Friday" is a manual job.
- [x] **Bulk CSV/Excel import & export** for products and inventory. Onboarding a business with
      2,000 SKUs currently means 2,000 API calls, and §9.9's plan limits mean the Growth-tier
      cap of 2,000 products is reachable by exactly the kind of tenant who will not enter them
      by hand.

### 9.29 Search & discovery depth

**done (2026-08-16), still not Atlas Search.** Faceted filtering (category, brand, tags, price range, in-stock, rating), seven sort orders, and paging. Facets are computed over the *same* filter as the listing, so a facet can't promise a count the listing then fails to deliver. **The regex-scan limitation from §9.5 is unchanged** — an unanchored `Regex.IsMatch` still cannot use an index, so search is a collection scan per query. Atlas Search remains the answer and remains unbuilt; `BestSelling` sorts by review count as an acknowledged proxy, since there is no sales counter on `Product`.
- [ ] §9.5 pushed search server-side via `Regex.IsMatch(field, Regex.Escape(search), IgnoreCase)`
      — correct and injection-safe, but an unanchored regex **cannot use an index**, so it is a
      full collection scan per search. It will be the first endpoint to fall over under load.
- [ ] No faceted filtering (price range, tags, in-stock, rating), no sort options on the public
      catalog (newest/price/popularity), no relevance ranking, no typo tolerance, no synonyms,
      no search-term analytics. Atlas Search is available on the cluster already in use and is
      the low-effort answer to most of this.

### 9.30 Storefront content management

**done (2026-08-16).** One polymorphic `ContentBlock` collection covering banners, static pages (About/Contact/**Terms**/**Privacy**), nav menu items and articles — with slugs, scheduling, publish state and SEO fields. Public read endpoints per type plus a nested menu builder. **`Business.CustomDomain` is still a field nothing reads** — domain verification, TLS provisioning and request routing are infrastructure work, not API work, and remain open.
- [x] A Business can set a logo, banner, theme colour and description — and that is the entire
      content model. No homepage layout, no promotional banners/slides with schedules, no
      static pages (About / Contact / Shipping Policy / **Terms** / **Privacy Policy** — the
      last two are legally required in most markets and there is nowhere to put them), no
      navigation menu builder, no blog/content marketing.
- [ ] `Business.CustomDomain` exists on the entity but nothing reads it — no domain
      verification, no TLS provisioning, no request-routing path. Custom domains are usually a
      paid-tier upsell; the field is currently a promise the platform does not keep.

### 9.31 COGS, gross margin & accounting correctness

**done (2026-08-16) — all three defects fixed.** Inventory is valued at cost (retail reported separately, never as an asset), products with no recorded cost are *counted* rather than silently valued at zero, COGS is written as its own ledger line at delivery from the `UnitCost` snapshotted on each `OrderItem`, and the P&L now reports `GrossProfit`/`GrossMarginPercent` alongside a `NetProfit` that finally subtracts what the goods cost. The balance sheet gained a liabilities side (tax payable, gift-card float) and a `NetPosition`. Verified live end to end: 225 order → revenue 205, COGS 120, gross profit 85 (41.46%), tax 20 carried as a liability, assets 585 at cost vs 800 at retail. **Still open:** multi-currency consolidation for a MultiBusiness tenant whose businesses trade in different currencies (§9.38).
Three concrete defects in the §9.16 accounting module, not just absences:
- [x] **Inventory is valued at retail price, not cost.** `InventoryService.GetValuationAsync`
      computes `Σ(StockQuantity × p.Price)` — the *selling* price. That figure feeds §9.16c's
      balance sheet as `TotalAssets`, so the balance sheet systematically overstates assets by
      the entire unrealised margin. Standard practice is cost (or lower-of-cost-or-market).
      Blocked on `Product.CostPrice` (§9.28).
- [x] **No COGS, so `NetProfit` is not net profit.** P&L computes
      `Revenue − Refunds − Expenses − DeliveryPayouts` and never subtracts what the goods cost.
      Unless a business happens to log every purchase as a manual `Expense` in the same window
      it sells in, the reported profit is inflated. Gross margin — the single number most
      retailers actually manage on — cannot be produced at all.
- [x] **No liabilities.** The balance sheet's own doc comment already admits it is partial. Tax
      collected (§9.19), gift-card float (§9.24), and supplier payables all belong on a side of
      the sheet that does not exist.
- [ ] Also missing: multi-currency consolidation for a MultiBusiness tenant whose businesses
      use different currencies (§9.38) — SuperOffice analytics currently sums raw decimals
      across businesses regardless of their `Currency`, which is only correct by accident.

### 9.32 BackOffice analytics

**done (2026-08-16).** `GET .../analytics/dashboard` — revenue, gross profit, order count, delivered/cancelled counts, AOV, unique/repeat/new customers, repeat rate, pending returns, low-stock count, a daily sales series, top products and a status breakdown. Revenue is recognised on `Delivered` only, deliberately the same rule §9.8 and §9.16a use, so the three numbers can never quietly disagree. Admin-tier, following §9.3's financial-data rule.
- [x] §9.8 built `GET /api/superoffice/analytics` for `TenantOwner` only. A `BusinessAdmin`
      running a single storefront — the platform's most common user — has **no dashboard
      endpoint at all**: no sales-over-time series, no order-status funnel, no
      average-order-value, no repeat-customer rate, no conversion signals, no top-products for
      their own business. They can list orders and add them up by hand.
- [x] Most of it is the same aggregation `AnalyticsService` already performs, re-scoped from
      "every business in the tenant" to "this business" and sliced by date. Cheap, high
      perceived value, and it removes the §9.9 asymmetry where BackOffice cannot even see its
      own plan usage.

### 9.33 Invoices & receipts

**done (2026-08-16), data only — no PDF.** `GET .../orders/{id}/invoice` assigns a gapless sequential number on first call via an **atomic** counter on the Business (two staff opening the same order cannot be handed the same number), then returns the same one forever. Carries seller legal identity, tax registration, both addresses, line items, discounts, tax label and amount, and paid/due. Rendering it as a PDF is left to the frontends — the data is the part that was missing. Packing slips and credit notes are still unbuilt.
- [ ] No invoice number (distinct from `OrderNumber`, and in many jurisdictions required to be
      gapless and sequential), no printable/PDF receipt, no tax invoice, no packing slip, no
      credit note for refunds. The order confirmation "email" is a log line (§9.10).
- [x] Depends on §9.19 for tax lines and §9.28/§9.30 for the seller identity details that must
      legally appear on the document.

### 9.34 Account verification — close the dead `PendingVerification` path

**done (2026-08-16) — the dead enum path is closed.** `EmailVerificationToken` (hashed, 3-day expiry, single-use, prior tokens retired on resend), `AppUser.EmailVerifiedAt`, `POST /api/auth/verify-email` and `POST /api/auth/resend-verification`. **`Auth:RequireEmailVerification` defaults to `false`, and that is a compromise, not an oversight:** the only `INotificationService` implementation logs instead of sending (§9.10), so defaulting it on would lock every new customer out of every shop until an operator read the server log. Turn it on in the same change that wires a real provider. Phone/OTP verification is still unbuilt — `PhoneVerifiedAt` exists as the field for it.
- [x] `UserStatus.PendingVerification` is the enum's default value and **nothing in the system
      ever produces it**: `AuthService.RegisterCustomerAsync`, `UserService.CreateStaffAsync`
      and `TenantService.SignupAsync` all explicitly set `Status = UserStatus.Active`, and
      `AuthService` rejects any non-`Active` user at login. So the state is unreachable, and
      every account — customer, staff, tenant owner — is created fully active with an
      unverified email address.
- [x] Consequences: signup spam with throwaway addresses, orders that cannot be contacted, and
      a password-reset flow (§9.10) whose entire security rests on an email nobody proved they
      own.
- [x] The infrastructure is already there — `PasswordResetToken` is exactly the right shape
      (hashed, expiring, single-use) to copy for a verification token, and `INotificationService`
      is the send path. This is a small, high-value item that mostly reuses §9.10's work.
- [ ] Phone/OTP verification is the same shape and matters more than email in the Bangladesh
      market this project's lineage points at; decide which is primary rather than building both.

### 9.35 Audit log & soft delete

**done (2026-08-16).** Soft delete is now platform-wide: `BaseEntity.IsDeleted`/`DeletedAt`/`DeletedByUserId`, with the filter applied inside `MongoRepository` so no Application-layer caller has to remember it, and `HardDeleteAsync` kept for tokens and idempotency records. Product/Category deletes retire their slug first so the name stays reusable past the unique index. The audit trail is written by one global `AuditLogFilter` rather than per-service calls — **a deliberate trade-off**: entries are HTTP-shaped (who, route, method, status, resource id, duration), not domain-shaped (before/after values), but a single interception point cannot be forgotten when a new endpoint is added, which is exactly how audit trails rot. Readable at `GET .../audit-log`, Admin-tier only. Login history / active-session listing is still unbuilt.
- [x] **Hard deletes everywhere.** `IMongoRepository.DeleteAsync` removes the document;
      `BaseEntity` has no `IsDeleted`/`DeletedAt`. Deleting a Product destroys the row that
      `StockMovement.ProductId` and inventory valuation point at — historical stock movements
      become orphans referencing an ID that no longer resolves. (Orders survive this by design:
      `OrderItem` snapshots name and price. Nothing else does.)
- [x] **No audit trail for anything but order status.** `StatusHistory`/`PaymentStatusHistory`
      are the only who-changed-what records in the system, and neither stores *who*. There is
      no record of who deleted a product, changed a price, edited an expense, blocked a
      customer, or altered a coupon — in a multi-staff BackOffice handling money, that is both
      an operational and a dispute-resolution gap.
- [ ] Also missing: an admin-visible login history / active-session list (`RefreshToken` has
      the data; nothing surfaces it) and per-business action log retention.

### 9.36 Lifecycle & marketing notifications

**done (2026-08-16).** `LifecycleNotificationService` sweeps abandoned carts, back-in-stock (via §9.26's wishlist), merchant low-stock alerts (closing the push §9.15b deferred) and post-delivery review requests, driven by an in-process `LifecycleNotificationWorker` (`Notifications:SweepIntervalMinutes`, 0 disables). Every sweep is idempotent on an "already notified" marker, so two instances running it send nothing twice. **`NotificationPreferences` was built before the sender, on purpose** — marketing mail honours per-channel consent, `MarketingConsentAt` records when opt-in happened, and `POST /api/auth/unsubscribe/{token}` works without a login. A guest cart has no recorded consent and is therefore treated as *not* opted in.
- [x] `INotificationService` fires on order confirmation and status change only. Absent, and
      all standard: abandoned-cart recovery (the highest-ROI automated email in e-commerce —
      `Cart` already persists with an `UpdatedAt`, so the query is trivial), back-in-stock
      alerts (needs §9.26's wishlist), low-stock alerts *to the merchant* (§9.15b explicitly
      deferred the push and left the signal query-only), review requests after delivery,
      shipping/tracking updates, and post-purchase follow-ups.
- [x] **No notification preferences and no unsubscribe.** The moment a real provider replaces
      `LoggingNotificationService`, sending marketing mail with no opt-out is a legal problem
      (CAN-SPAM / GDPR), not just a rude one. Build the preference model *before* the provider,
      not after.

### 9.37 Privacy & data rights

**done (2026-08-16).** Customer data export, right-to-erasure by **anonymisation in place** (PII overwritten, the document kept — orders reference the id and the merchant has its own duty to retain them), notification preferences with consent timestamps, token-based unsubscribe, and a tenant-wide export for offboarding that strips password hashes even from the owner's own copy. **Still open:** cookie-consent surfaces (frontend), a written PII retention policy, and the PCI-scope decision, which should be recorded before §9.6 picks a gateway.
- [ ] No data export ("download my data"), no account deletion or anonymisation, no consent
      capture, no PII retention policy, no cookie-consent surface for the storefronts. The
      platform stores customer names, emails, phones and full shipping addresses across
      unrelated tenants.
- [x] Tenant offboarding has the same hole from the other direction: `TenantStatus` can be
      changed, but there is no "export everything and delete this tenant" path — so a
      subscriber who cancels cannot get their data out or have it removed. For a platform
      *sold as a subscription*, that is a contractual exposure, not just a feature gap.
- [ ] Payment-data handling rules (PCI scope) need deciding *before* §9.6 picks a gateway —
      the answer should be "we never touch card data, the gateway does", but it should be a
      recorded decision.

### 9.38 Multi-currency & localization

**partially done (2026-08-16).** `Order.Currency` is snapshotted at checkout and every `LedgerEntry` written for an order takes its currency from that snapshot — so changing a Business's currency can no longer retroactively reinterpret closed history. That was the one-field fix flagged as not needing to wait. **Everything else is still open:** no FX rates, no per-currency pricing, no multi-language product content, no RTL handling. SuperOffice analytics still sums raw decimals across businesses regardless of currency (§9.31).
- [x] `Business.Currency` is a display label and nothing more: no FX rates, no per-currency
      pricing, and `Order` **does not snapshot the currency at all** — a business that changes
      its `Currency` field retroactively reinterprets every historical order and every
      `LedgerEntry` written before the change. `LedgerEntry` does carry a `Currency`; `Order`
      does not. Snapshotting currency onto `Order` is a one-field fix and should not wait for
      the rest of this item.
- [ ] No multi-language product content, no RTL consideration, no per-locale formatting. The
      platform's obvious first market is bilingual (Bangla/English) and the catalog is
      single-string-per-field.

### 9.39 Tenant-facing webhooks & public API keys

**done (2026-08-16).** Outbound webhooks with HMAC-SHA256 signatures, HTTPS-only endpoints, per-delivery audit rows a tenant can debug against, and auto-disable after 10 consecutive failures. Six event names published from real domain transitions. `ApiKey` gives server-to-server credentials with a public key id + hashed secret, coarse read/write scopes, expiry and revocation, compared in fixed time. **Note:** `IApiKeyService.AuthenticateAsync` exists and is tested, but no authentication *handler* is wired into the pipeline yet — API keys can be issued and validated, not yet used to authenticate a request. That is the remaining step.
- [x] No way for a subscriber to integrate anything: no outbound webhooks (`order.created`,
      `order.delivered`, `product.low_stock`), no per-tenant API keys for server-to-server
      access (the only credential is a 30-minute user JWT), no scoped tokens, no public API
      documentation beyond Swagger.
- [x] This is what turns Vastora from a closed product into a platform, and it is the usual
      justification for the top pricing tier. It also unblocks the tenant's own automations —
      accounting exports, ERP sync, Meta/Google catalog feeds — without Vastora building each
      integration itself.

### 9.40 Production readiness

**partially done (2026-08-16).** Landed: a multi-stage non-root `Dockerfile` with a real health check, a GitHub Actions CI workflow (build with `-warnaserror`, test, docker build), per-endpoint rate limiting on the credential routes (10/min vs the global 100/min), and the query-path indexes from §9.18. **Still open, and each needs a decision rather than more code:** cloud file storage (the `IFileStorageService` seam is ready, the provider is not chosen), image processing/CDN, integration tests (still blocked on Docker in this environment), a deployment target and registry push, backup/restore and DR drills, a real secret store plus rotating the Atlas connection string in `.env`, and APM/error tracking/uptime alerting. CORS remains open by default — the lever exists, nothing pulls it.
Consolidates the infrastructure gaps already scattered through §7's "Deliberate
simplifications" plus what the audit added, so there is one checklist to clear before real
traffic:
- [ ] **Cloud file storage.** `LocalFileStorageService` writes to local disk — product images
      do not survive a redeploy on most PaaS hosts and cannot be shared across instances. The
      `IFileStorageService` seam exists precisely so S3/Azure Blob/Cloudinary is a swap; make
      the swap before the first real catalog is uploaded, not after it is lost.
- [ ] **No image processing** — no resizing, thumbnails, WebP conversion or CDN. Storefront
      page weight is a conversion and SEO factor; today the browser downloads whatever the
      merchant uploaded, at full size, from the app server.
- [ ] **CORS is still open by default** (`Cors:AllowedOrigins` unset ⇒ `AllowAnyOrigin`). Set it.
- [x] **Rate limiting is global, not per-tenant or per-endpoint.** 100 req/min per IP protects
      the process, but one tenant's traffic spike degrades every other tenant, and login/
      password-reset get the same budget as catalog reads. Per-endpoint limits on the auth
      routes are the minimum.
- [ ] **Integration tests** — §9.12's remaining item; still blocked on Docker for
      Testcontainers. §9.17's transaction work in particular cannot be trusted without a real
      MongoDB to run it against.
- [ ] **No CI/CD pipeline, no staging environment, no deployment target chosen, no
      containerisation** — there is no `Dockerfile` and no workflow file in the repo.
- [ ] **No backup/restore or disaster-recovery procedure** for a database holding many
      unrelated businesses' commercial records, and no documented restore drill.
- [ ] **Secrets are in `.env`.** Fine locally; production needs a real secret store, and the
      Atlas connection string currently committed to the developer's `.env` (§6) should be
      rotated before launch.
- [ ] **No APM / error tracking / uptime alerting.** Serilog logs to console only (§9.11) — in
      production that means nobody finds out about an outage from the system itself.

---

### 9.41 Customer profile customization — done (2026-08-17)

Audited what a Customer (the shop's end user, not BackOffice staff) could actually do to their
own account before this. Answer: rename themselves, change their phone number, and manage
notification preferences (§9.36) — `UpdateProfileRequest` was `(FullName, Phone)` and nothing
else, `AppUser.Addresses` existed in the data model but had zero endpoints, there was no
authenticated password-change (only the token-based forgot/reset flow, which assumes the user is
already locked out), and no avatar of any kind.

- [x] **Authenticated password change.** `POST /api/auth/me/change-password`
      `{ currentPassword, newPassword }`, any authenticated role (not Shop-only —
      `/api/auth/*` is the shared realm, §4). Verifies the current password via the same
      `IPasswordHasher` the login path uses, then revokes every active `RefreshToken` for that
      user — the same "something changed, sign out everywhere" behavior as a token-based reset,
      just without needing a token because the caller already proved who they are.
- [x] **Avatar.** `AppUser.AvatarUrl` (new field), uploaded via `POST /api/auth/me/avatar`
      (`multipart/form-data`, same 5 MB limit and jpeg/png/webp/gif whitelist as product images,
      reusing `IFileStorageService` exactly as `ProductsController` does — files grouped under
      the caller's `BusinessId`, or a shared `"platform"` bucket for a Tenant-/Platform-level
      account with no single Business). `DELETE /api/auth/me/avatar` clears it. Both return the
      enriched `UserSummaryResponse` below.
- [x] **`UserSummaryResponse` enriched.** Was 7 fields (`Id/FullName/Email/Role/TenantId/
      BusinessId/Status`); now also carries `Phone`, `AvatarUrl`, `EmailVerifiedAt`,
      `PhoneVerifiedAt`, `CreatedAt` — all of it already existed on `AppUser`/`BaseEntity`, just
      wasn't mapped out. One shared `UserSummaryResponse.From(AppUser)` factory now backs every
      construction site (`UserService.Map`, `AuthTokenIssuer.IssueAsync`) so the two can't drift
      out of sync the way two independent inline mappers eventually do.
- [x] **Saved address book**, closing the gap flagged in the previous session. `Address` gained
      an `Id` (empty/unused on the one-off address snapshotted onto an `Order` at checkout —
      only meaningful for an entry living in `AppUser.Addresses`). Full CRUD under
      `/api/shop/account/addresses` (`GET`, `POST`, `PUT /{addressId}`, `DELETE /{addressId}`),
      Customer-only, scoped from the JWT like the rest of `ShopAccountController`. The first
      address saved is always the default regardless of what's requested — there is no
      sensible "no default" state once at least one address exists — and deleting the current
      default promotes another one automatically rather than leaving the book defaultless.
      Deliberately **not** wired into checkout itself this pass: `CheckoutRequest.shippingAddress`
      still takes an inline address, same as before — a "pick a saved address" convenience on
      the checkout form is a frontend-side lookup against this new list, not a backend change.
- [ ] **Still not built, and deliberately not faked:** phone verification. `PhoneVerifiedAt`
      exists on `AppUser` with no writer anywhere — same category as §9.10's "no real email/SMS
      provider chosen" gap. A phone can't receive a clickable link the way email verification
      does; it needs an OTP flow behind an SMS provider, and building the endpoint shape without
      a provider to actually deliver a code would just be a UI that always fails silently.

---

### 9.42 Image upload for Category, Business logo/banner, and content blocks — done (2026-08-18)

The BackOffice frontend had already wired up calls to `POST /categories/{id}/image`,
`POST /businesses/{id}/logo`, `POST /businesses/{id}/banner`, and `POST /content/{id}/image` —
none of them existed yet; `Product.Images` (§9.5) and `AppUser.AvatarUrl` (§9.41) were the only
fields with a real upload path, everything else (`Category.ImageUrl`, `Business.LogoUrl`/
`BannerUrl`, `ContentBlock.ImageUrl`) was write-only-as-a-plain-string. Built all four, each
matching the guessed path exactly:
- [x] `POST /api/businesses/{businessId}/categories/{categoryId}/image` →
      `CategoryService.SetImageAsync` (new — sets `ImageUrl` only, doesn't force resending the
      whole category the way a `PUT` would).
- [x] `POST /api/businesses/{businessId}/logo` and `.../banner` → `BusinessService.SetLogoAsync`/
      `SetBannerAsync` (new), restricted to Admin/TenantOwner/Platform — matching `PUT
      .../businesses/{id}`'s existing Staff exclusion, since both edit the same profile.
- [x] `POST /api/businesses/{businessId}/content/{blockId}/image` → `ContentService.SetImageAsync`
      (new), on the existing Merchandising controller alongside the rest of §9.30.
- [x] **Extracted `ImageUploadPolicy`** (`Vastora.Application.Common`) — the content-type
      whitelist/size-limit was about to be hand-copied a third and fourth time (Products and the
      avatar endpoint already each had their own identical copy); `ProductsController` and
      `AuthController` were refactored onto the shared one in the same change so there's one
      definition instead of four.

All four follow the exact shape `ProductsController.UploadImage` established: one
`multipart/form-data` field named `file`, 5 MB limit, jpeg/png/webp/gif only, local disk storage
via `IFileStorageService`, and a dedicated `SetXAsync`/`AddImageAsync`-style service method so an
upload never forces the caller to resend the whole entity. Six new tests (two per new service
method, proving the image field changes and nothing else does). No entity gained a competing
"only settable one way" rule — `ImageUrl`/`LogoUrl`/`BannerUrl` are still plain strings on their
create/update DTOs too, for a frontend that already has a URL and doesn't need to upload a file.

---

### 9.43 Discounts unified — visibility, cart-level gift cards/store credit, real expiry — done (2026-08-18)

Audited every discount surface (`Coupon`, `Promotion`, `GiftCard`, `StoreCreditEntry`) against the
four things a client asked for: one place documenting all of it, every customer-facing API a
storefront needs actually existing, codes "shown where applicable" with a way to *hide* one behind
a targeted email instead, and expiry that's actually correct. The engine itself (§9.23, §9.24) was
already solid — this closed real gaps in wiring and design, it didn't rebuild it.

- [x] **`DiscountVisibility` (`Public`/`Hidden`)** on both `Coupon` and `Promotion`. Public is
      listed by the new available-offers endpoint below; Hidden only works when the exact code is
      typed — it never appears there. Nothing about evaluation/redemption changed: a Hidden code
      is validated and priced exactly like a Public one, `ApplyCouponAsync`/`ApplyPromotionCodeAsync`
      don't know or care which it is. Visibility governs *discoverability*, not *validity*.
- [x] **`GET /api/shop/cart/available-offers`** (`ICartService.GetAvailableOffersAsync` →
      `IPricingService.GetAvailableOffersAsync`) — "shown where applicable": merges
      `ICouponService.GetPublicActiveAsync` and `IPromotionService.GetPublicLiveAsync`, filtered to
      what the cart's current subtotal can even qualify for on `MinOrderAmount` (a full
      customer-group/first-order check still happens when the code is actually applied — this is a
      shortlist, not a second source of truth). Each `AvailableOfferResponse` carries a
      server-computed `Summary` ("10% off", "$5 off orders over $50", "Free shipping", "Buy 2, get 1
      free") so every client describes an offer identically rather than re-deriving the wording
      from `DiscountType`/`PromotionEffect` itself.
- [x] **`POST /api/businesses/{businessId}/discount-emails`** (new `IDiscountEmailService`) — the
      delivery mechanism a Hidden code needs, since visibility alone doesn't tell anyone it exists.
      Resolves a code against `Coupon` then `Promotion`, unions an explicit `CustomerUserIds` list
      with a `CustomerGroupId`'s membership, and sends through the existing `INotificationService`/
      `EmailTemplates` pipeline (new `EmailTemplates.DiscountCode`) — respecting
      `NotificationPreferences.MarketingEmail` exactly like every other marketing send (§9.36).
      "The customer can only find out by email" is not license to email someone who opted out.
- [x] **Gift cards and store credit were write-only on the cart before this.**
      `Cart.GiftCardCodes` and the checkout-only `UseStoreCredit` flag existed, but
      `CartService.MapAsync` hard-coded an empty gift-card list and `false` into every
      `PricingContext` it built for a cart preview — a shopper who applied a gift card or opted
      into store credit saw *no* discount until checkout actually charged them, and could not tell
      in advance what they'd really owe. Fixed by threading `cart.GiftCardCodes`/`cart.UseStoreCredit`
      into the same `PricingContext` checkout already used, and adding the endpoints that were
      missing to actually set them pre-checkout: `POST/DELETE /api/shop/cart/gift-cards[/{code}]`
      (validated against the ledger via `IGiftCardService.CheckBalanceAsync` before it sticks) and
      `PUT /api/shop/cart/store-credit`. `CartResponse` now carries `giftCardCodes`, `giftCardTotal`,
      `useStoreCredit`, `storeCreditApplied` and `amountDue`, so the preview and the charge cannot
      disagree — the same guarantee §9.22 already gave coupons and promotions.
- [x] **`Coupon.ExpiresAt` is now nullable.** It was a required `DateTime`, forcing every coupon —
      including a permanent referral or partner code — to carry an expiry date it didn't actually
      have, or to be re-issued with a far-future date as a workaround. Null now means what it says:
      never expires. `IsValidNow` and the create/update validators were updated to match; nothing
      about an existing coupon's stored `ExpiresAt` changed.
- [x] **`StoreCreditEntry` gained expiry**, which didn't exist in any form before — every credit
      was permanent regardless of source, so a "welcome bonus expires in 30 days" promotional grant
      was not expressible. `ExpiresAt` (nullable) only matters on a positive entry; `RecordAsync`
      drops it entirely for a debit. Computed live via `StoreCreditEntry.CountsTowardBalance(now)` —
      the same pattern `Coupon.IsValidNow`/`GiftCard.IsRedeemableNow`/`Promotion.IsLiveNow` already
      use — so `GetBalanceAsync`/`GetStatementAsync` are correct at read time with **no sweep job**,
      and the ledger stays genuinely append-only (an expired credit's entry is never mutated or
      offset, it simply stops counting). A refund settlement (§9.21) still never expires — only a
      deliberately-timed grant does, via the new optional `expiresAt` on
      `MerchandisingController.GrantStoreCredit`.
- [x] Eighteen new tests (93 → 111) across `CouponServiceTests`, `StoreCreditServiceTests`,
      `PromotionServiceTests` (visibility filtering), `DiscountEmailServiceTests` (union, opt-out,
      unknown code, no recipients), `CartServiceTests` (the gift-card/store-credit preview fix,
      proven end-to-end against real `PricingService`) and `PricingServiceAvailableOffersTests`.

---

### 9.44 Cart delivery fee — visible in the preview, and fulfillment-aware — done (2026-08-18)

A client reported the cart response never said anything about delivery fee even after a coupon
was applied, and asked why a Pickup order would presumably still get charged one. Both turned out
to be real bugs, not just missing documentation — found while investigating.

- [x] **`CartService.MapAsync` was zeroing the delivery fee, silently, on every call.**
      `PricingContext.ExplicitDeliveryFee` is `decimal?` specifically so "no override, resolve the
      real fee" can be expressed as `null` — but the cart-preview call was passing the *literal*
      `0m`, which `ShippingService.ResolveFeeAsync` treats identically to a staff member
      deliberately overriding the fee to zero. The business's `DefaultDeliveryFee` and its
      shipping zones were never even consulted for a cart preview; checkout (a separate call
      site) was unaffected; only `GET /api/shop/cart` was wrong. Fixed by passing `null`.
      `CartResponse` also had no field to put a delivery fee on even once resolved correctly —
      added `deliveryFee`, `shippingMethodName`, and `shippingOptions`, filled from the same
      `IPricingService.PriceAsync` call `discounts`/`discountTotal` already came from.
- [x] **`FulfillmentMethod` never reached pricing at all, at either call site.** It existed on
      `CheckoutRequest` and got stored onto the resulting `Order` as a label, but
      `OrderService.BuildPricingContext` never passed it into `PricingContext` — so a customer who
      chose `Pickup` at checkout was charged exactly the same delivery fee a `Delivery` order
      would have been, because `ShippingService.ResolveFeeAsync` ran unconditionally regardless of
      what the customer actually selected. This was a real checkout bug, not just a missing cart
      field. Fixed by threading `request.FulfillmentMethod` through, and by gating
      `PricingService.PriceAsync`'s shipping-quote/fee resolution on it: `Pickup` and `Digital`
      (no physical delivery leg) now always price at `deliveryFee = 0` with an empty
      `shippingOptions` list, never a shipping-zone or `DefaultDeliveryFee` amount; `Delivery` and
      `ExternalCourier` are unaffected.
- [x] **`Cart.FulfillmentMethod`** (new, default `Delivery`) plus
      `PUT /api/shop/cart/fulfillment-method` — lets a shopper declare Pickup *before* checkout so
      the preview is honest about it, the same relationship `Cart.UseStoreCredit` (§9.43) already
      has to `CheckoutRequest.UseStoreCredit`: preview-only, and `CheckoutRequest.FulfillmentMethod`
      remains the one that's actually charged — repeating the same choice there is still required.
      Deliberately zeroed rather than merely defaulted to `Delivery`-equivalent zero: a `Pickup`
      cart shows `deliveryFee: 0` for the same reason a `Delivery` cart with genuinely free
      shipping does, and the two would otherwise be indistinguishable to a frontend that only
      looks at the number. `fulfillmentMethod` being present on `CartResponse` is what disambiguates
      "no delivery fee because it's free" from "no delivery fee because there's no delivery".
- [x] Seven new tests (111 → 118): `PricingServiceFulfillmentTests` (the four `FulfillmentMethod`
      values, parameterized, proving only `Delivery`/`ExternalCourier` ever charge) and three new
      `CartServiceTests` cases proving the preview now shows the business's real
      `DefaultDeliveryFee` and that switching to `Pickup` drops it to zero with no shipping
      options, where before this session there was no field to even make that assertion against.

Not touched, and worth flagging rather than silently leaving inconsistent: `CheckoutRequest.
ShippingAddress` is still a required field at checkout even when `FulfillmentMethod` is `Pickup` —
a customer picking up in-store must still submit a (possibly nominal) address object today. That's
a separate, smaller gap from the two above and wasn't part of what was reported; flagged here so
it isn't mistaken for fixed.

---

### 9.45 Cart coupon removal — the missing symmetric DELETE — done (2026-08-18)

Found while cross-checking the live Swagger spec against the Antivaly frontend: every other
cart-level code type had a matching removal endpoint — `DELETE /api/shop/cart/promotions/{code}`,
`DELETE /api/shop/cart/gift-cards/{code}` — but the original, single-slot `CouponCode` never got
one. A shopper could `POST /api/shop/cart/coupon` to apply a code and then had no way to take it
back off short of `DELETE /api/shop/cart` (clearing the entire basket, items included).

- [x] `ICartService.RemoveCouponAsync(businessId, owner, ct)` / `CartService` — sets
      `Cart.CouponCode = null` and re-prices, same shape as `RemovePromotionCodeAsync`.
- [x] `DELETE /api/shop/cart/coupon?businessId=` — `[AllowAnonymous]`, matching every other cart
      mutation endpoint (guest and Customer both); no route parameter needed since a cart only
      ever holds the one coupon slot.
- [x] One new test, `RemoveCouponAsync_ClearsTheAppliedCoupon` (118 → 119).

---

## 10. Progress Log

Newest entry first. Keep entries short — what happened and why, not a diff.

### 2026-08-18 — Cart coupon removal — the missing symmetric DELETE (§9.45)
Caught by cross-checking the live Swagger spec against the Antivaly frontend: `DELETE
/api/shop/cart/promotions/{code}` and `DELETE /api/shop/cart/gift-cards/{code}` both exist, but
the original single-slot coupon never got a matching removal endpoint — only `POST .../coupon`
did. Added `ICartService.RemoveCouponAsync`/`CartService` (nulls `Cart.CouponCode`, re-prices) and
`DELETE /api/shop/cart/coupon?businessId=`, `[AllowAnonymous]` like every sibling cart-mutation
endpoint. One new test (118 → 119). Updated the Antivaly Shop blueprint in the same session.

### 2026-08-18 — Cart delivery fee visible in the preview, and fulfillment-aware (§9.44)
A client reported the cart API said nothing about delivery fee, even after a coupon was applied,
and separately expected a Pickup order to skip it. Investigating turned up two real bugs, not one
documentation gap: `CartService.MapAsync` was passing the pricing call `0m` instead of `null` for
`ExplicitDeliveryFee`, which forces the fee to resolve as an explicit zero rather than actually
consulting `Business.DefaultDeliveryFee` or shipping zones — so the cart preview's delivery fee
was silently wrong even before considering that `CartResponse` had no field to put it on at all.
Separately, `FulfillmentMethod` never reached `PricingContext` at either call site — a customer
choosing `Pickup` at real checkout was still charged the resolved delivery fee, because nothing
gated `ShippingService.ResolveFeeAsync` on it. Fixed both: `PricingService.PriceAsync` now skips
shipping quotes and fee resolution entirely for `Pickup`/`Digital`; added `Cart.FulfillmentMethod`
and `PUT /api/shop/cart/fulfillment-method` so the preview can reflect that choice before checkout
(mirroring how `UseStoreCredit` already works — preview-only, `CheckoutRequest.FulfillmentMethod`
is still what's actually charged); and added `deliveryFee`/`shippingMethodName`/`shippingOptions`/
`fulfillmentMethod` to `CartResponse` so there's finally something to render. Seven new tests
(111 → 118). Updated the Antivaly Shop blueprint in the same session — this changes what the
storefront needs to send and render.

### 2026-08-18 — Discounts unified: visibility, cart-level gift cards/store credit, real expiry (§9.43)
A client asked for one section covering every discount feature (coupons, gift cards, store
credit), every customer API that needs actually existing, industry-grade "shown where applicable"
plus a way to *hide* a code behind a targeted email, and correct expiry throughout. Added
`DiscountVisibility` (Public/Hidden) to `Coupon` and `Promotion`; a new
`GET /api/shop/cart/available-offers` that lists Public, currently-qualifying codes (Hidden ones
never appear, but still redeem normally when typed); a new `IDiscountEmailService` and
`POST .../discount-emails` that emails a code to named customers and/or a `CustomerGroup`,
respecting marketing opt-out. Found and fixed a real bug along the way: `Cart.GiftCardCodes` and
`UseStoreCredit` were set-but-never-read — `CartService.MapAsync` always priced the preview with
an empty gift-card list and `false`, so a shopper applying either saw no discount until checkout
actually charged them. Added the missing cart endpoints (`gift-cards`, `store-credit`) and wired
them into the same `PricingContext` checkout uses, so the preview and the charge agree. Made
`Coupon.ExpiresAt` nullable (an evergreen code is legitimate, not an oversight) and gave
`StoreCreditEntry` an optional `ExpiresAt`, computed live the same way every other expiry in this
codebase already is — no sweep job. Eighteen new tests. Updated the BackOffice and Antivaly Shop
blueprints in the same session (their own intro notes require it).

### 2026-08-18 — Image upload for categories, business logo/banner, content blocks (§9.42)
The BackOffice frontend had already guessed at four upload endpoints and wired calls to them;
only `POST /products/{id}/images` was real. Built the other four to match the guessed paths
exactly: category image, business logo, business banner, content-block image — each a new
`SetImageAsync`/`SetLogoAsync`/`SetBannerAsync` service method plus a controller endpoint
mirroring `ProductsController.UploadImage`'s shape precisely (multipart file, 5 MB limit,
jpeg/png/webp/gif whitelist, `IFileStorageService`). Also extracted `ImageUploadPolicy` since the
whitelist dictionary was about to be copied a third and fourth time — Products and the §9.41
avatar endpoint each already had their own identical copy; both now share the one definition.

### 2026-08-18 — Category browsing now includes subcategory products (§9.5)
Closed the gap flagged in the last two sessions: clicking a parent category (e.g. "Electronics")
showed nothing from its subcategories ("Phones"), because `ProductService.QueryCatalogAsync`
matched `?categoryId=` exactly. Added `ResolveCategoryAndDescendantIdsAsync` — walks
`Category.ParentCategoryId` the same way `CategoryService.GetTreeAsync` does, flattened to an id
set — and swapped the exact-match predicate for a `Contains` against it. Both `GET .../products`
and `.../products/facets` share the one filter method, so the fix and its facet counts came for
free together: browsing a parent category's facets now break down by subcategory automatically,
which is the data a "subcategory filter" UI needs — no new endpoint. Picking a specific
subcategory still narrows to an exact match, since a leaf category has no descendants to expand
into. One test added proving the three-way split (parent shows both, child shows only its own).

### 2026-08-17 — Customer profile customization: password change, avatar, address book (§9.41)
Asked whether Customer users had profile-customization APIs beyond `UpdateProfileRequest`'s
`FullName`/`Phone`. They didn't have much: no authenticated password change (only the
token-based forgot/reset flow), no avatar, and — closing the gap flagged in the previous
session — `AppUser.Addresses` still had zero endpoints despite existing in the data model.
Added all three: `POST /api/auth/me/change-password` (verifies current password, revokes every
session on success — mirrors the existing reset-password "something changed" behavior);
`POST`/`DELETE /api/auth/me/avatar` reusing `IFileStorageService` exactly as product images do;
and full address-book CRUD under `/api/shop/account/addresses`, with `Address` gaining an `Id`
(meaningful only for a saved-book entry, unused on the one-off address an order snapshots at
checkout). `UserSummaryResponse` grew from 7 fields to include `Phone`/`AvatarUrl`/
`EmailVerifiedAt`/`PhoneVerifiedAt`/`CreatedAt`, backed by one shared `.From(AppUser)` factory so
its two construction sites (`UserService`, `AuthTokenIssuer`) can't drift apart. Left alone on
purpose: phone verification, since there's no SMS provider to deliver an OTP to — same "not
inventing infrastructure without a chosen vendor" rule §9.10 already applies to email.

### 2026-08-17 — Branded HTML email templates for every send point (§9.10)
Asked for "professional standard" templates for every email the platform sends, with the
Business's logo in the body, plus the Antivaly shop blueprint brought up to date on profile and
order-tracking endpoints. Every one of the ~11 email moments in the codebase (verification,
password reset, order confirmation/status/shipped, return decision, refund issued, abandoned
cart, back-in-stock, merchant low-stock, review request) now renders through one shared HTML
layout (`EmailTemplates.cs`) that pulls in the Business's logo/name/brand color, with a
plain-text fallback in the same message. Password reset and verification now build a real
clickable link off `PublicBaseUrl` instead of handing back a bare token to paste in by hand.
Delivery itself is unchanged — still SMTP-if-configured, server-log otherwise (§9.10) — only the
content changed. Auditing the shop blueprint against the actual controllers surfaced two real
gaps, documented rather than silently implemented since neither was asked for: `AppUser.Addresses`
exists in the data model and is exported/anonymised, but no endpoint ever lets a customer add to
it, so there's no address book to build a UI against; and order tracking is `GET`-only with no
SMS/push channel, so a "text me updates" toggle would have nothing to call. `docs/
ANTIVALY_SHOP_BLUEPRINT.md` §6.1 also picked up three routes it was missing entirely
(`verify-email`, `resend-verification`, `unsubscribe/{token}`).

### 2026-08-17 — Categories can be re-parented after creation (§9.5)
Asked whether subcategories were supported. They were, end to end (`ParentCategoryId`, the
`/tree` endpoint, both frontend blueprints already documenting it) — except a category's parent
could only ever be set at creation; `UpdateCategoryRequest` had no way to move it later. Added
`ParentCategoryId` to `UpdateCategoryRequest` and cycle-guarding in `CategoryService.UpdateAsync`
(rejects self-parenting and parenting under one's own descendant, both 400s). BackOffice blueprint
§7.3 updated with the new field and the parent-picker guidance. Left alone on purpose: the shop's
`?categoryId=` catalog filter still matches exactly, so browsing "Electronics" won't surface
"Phones" products — that's a separate, larger change (descendant-id expansion in
`ProductService.QueryCatalogAsync`) that wasn't asked for this round.

### 2026-08-16 — §9B implemented: 55 of 77 commerce-completeness items, verified end to end
Asked to implement the rest of the roadmap and update the frontend blueprints. Did §9.17 through
§9.40 in one session. Per-section detail lives in each §9.x status line above — this entry is the
narrative and the honesty ledger.

**The four defects the audit found are fixed, and each has a regression test.**
- **Checkout is atomic now** (§9.17). Validate-all-first, then guarded atomic `$inc` per line via
  a new `IProductStockStore`, then compensating rollback if anything fails after stock moved.
  Verified live: a two-line cart whose second line was short left the first line's stock exactly
  where it started, with no order written. Idempotency-Key replay verified too — same order
  number back, stock unchanged.
- **Inventory is valued at cost** (§9.31). `Product.CostPrice` added, `UnitCost` snapshotted onto
  every `OrderItem`, COGS written as its own ledger line at delivery, and the balance sheet given
  a liabilities side. Verified live: a 225.00 order produced revenue 205.00, COGS 120.00, gross
  profit 85.00 at 41.46%, tax 20.00 carried as a liability, assets 585.00 at cost against 800.00
  at retail — arithmetic checked by hand.
- **`PendingVerification` is reachable** (§9.34), behind `Auth:RequireEmailVerification`, which
  defaults **off** because the only notification implementation still logs instead of sending.
- **Pagination exists** (§9.18), with the compound indexes the paged reads need.

**Two bugs found by actually running it, not by the compiler.** `UpdateBusinessRequest` never
carried the new tax/invoicing settings, so a 200 response was silently discarding them; and the
idempotency filter tried to hash the action's `CancellationToken`, which 500s on every request
carrying the header. Both fixed and re-verified. This is the argument for smoke-testing over
trusting a green build.

**What was not done, and why.** 22 items stay unchecked. Most need a vendor or product decision
rather than code — gateway, email provider, cloud storage, CDN, deployment target, secret store,
APM. Integration tests are still blocked on Docker. The rest were scoped down deliberately: Atlas
Search, per-product price lists, partial shipment, PDF rendering, loyalty points, product Q&A,
multi-language content, custom-domain routing. One is a genuine half-step and is flagged as such:
§9.39 issues and validates API keys but no authentication handler consumes them yet.

**Also deliberate:** compensating rollback rather than MongoDB transactions in checkout (sessions
would have to thread through every repository call — the trade-off is documented at the call
site), and an HTTP-shaped audit log written by one global filter rather than domain-shaped
before/after values written per service (a single interception point cannot be forgotten when a
new endpoint is added).

Tests went 37 → 78, all passing. `dotnet build` is clean. The API was run against the live Atlas
database and the whole path — tenant signup, tax config, cost-priced product, guest cart,
promotion, preview, checkout, idempotent replay, delivery, P&L, invoice, dashboard, balance
sheet, guest lookup, RMA with restock-on-receipt, review, wishlist — was exercised with curl.
All three frontend blueprints were updated in the same session, per the rule at the top of this
file: the paged-envelope and catalog-query changes are breaking, and the docs would have drifted
immediately otherwise.

### 2026-08-16 — Commerce-completeness audit: §9.17–§9.40 added to the roadmap
No code written. Asked to research what the project lacks as a *best-in-class* e-commerce
application and record it as todos. Audited the existing code against what mature commerce APIs
(Medusa, Saleor, commercetools, Shopify) treat as core, and added 24 new roadmap sections.

Every claim in them was checked against the source, not assumed from this document — which is
how the four items worth calling out here were found, all of them defects rather than missing
features:
- **Checkout is not atomic** (§9.17). `CheckoutAsync` decrements stock inside the item loop,
  before the order is written, with no transaction and no rollback — a mid-loop failure leaves
  earlier items permanently deducted. Same method also has a read-then-write oversell race and
  no idempotency key.
- **Inventory is valued at retail price** (§9.31). `GetValuationAsync` uses `p.Price`, and that
  number is the balance sheet's `TotalAssets` — so the balance sheet overstates assets by the
  full unrealised margin. There is no `CostPrice` field to fix it with, and consequently no COGS
  in the P&L, so `NetProfit` isn't net profit.
- **`UserStatus.PendingVerification` is unreachable** (§9.34). It's the enum default, but all
  three account-creation paths explicitly set `Active`. Nobody's email is ever verified,
  including the address the password-reset flow trusts.
- **No pagination exists anywhere** (§9.18). `IMongoRepository<T>` has no skip/take/sort at all,
  so every list endpoint returns the whole collection and sorts it in memory.

The rest are genuine absences with no code to blame: tax, returns/RMA and partial refunds,
shipping zones and carrier tracking, variant-aware cart/order, a promotions engine beyond a
single coupon code, gift cards, reviews, wishlist, guest checkout, product cost/SEO/weight
fields and bulk import, faceted search, storefront CMS, BackOffice analytics, invoices, audit
log and soft delete, lifecycle notifications, privacy/data-rights, multi-currency, tenant
webhooks, and a production-readiness checklist that consolidates the infrastructure gaps §7
had scattered across its "deliberate simplifications" list.

Deliberately *not* done in this session: reordering or renumbering §9.1–§9.16, and any attempt
to estimate the new items. The suggested order in the audit note is a recommendation about
severity, not a schedule.

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
