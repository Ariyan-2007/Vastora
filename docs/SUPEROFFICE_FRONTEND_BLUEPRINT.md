# SuperOffice Frontend Blueprint

**Self-contained.** This file assumes no other context — move it into the SuperOffice
project's own repo and hand it to a fresh session; everything needed to build against the
Vastora API is here. If the Vastora API changes, this doc must be updated in the same session
as the change (see the intro note at the top of the main `VASTORA_BLUEPRINT.md` in the Vastora
backend repo).

---

## 1. What this app is

**SuperOffice** is the cross-business control panel for a **`TenantOwner`** — the owner of a
Vastora subscription that runs **more than one Business** under one account (a group/holding
company selling different product lines under different brand names, one owner). It is the
*only* screen that can see every Business a Tenant owns at once.

### Where it sits in the platform

Vastora is a multi-tenant e-commerce API platform sold as a subscription. Three tiers:

```
Platform (Vastora itself — not this app)
  └─ Tenant (the paying subscriber — this app's user is the Tenant's owner)
       └─ Business (one storefront: Landing Page + Shop + BackOffice — not this app either)
```

A Tenant subscribed as `MultiBusiness` gets this app in addition to a separate **BackOffice**
app per Business (day-to-day catalog/order management — see `BACKOFFICE_FRONTEND_BLUEPRINT.md`
for that one; it's a different app, different repo). A `SingleBusiness` Tenant never sees
SuperOffice at all — they only ever get one Business, managed directly through BackOffice.

**Scope of this app, precisely:** Business-level oversight (list, create, edit profile,
suspend/activate) across the whole Tenant, plus viewing the Tenant's own subscription info.
It does **not** manage products, orders, staff, or coupons — that's BackOffice's job, one
Business at a time. A `TenantOwner`'s JWT is valid on BackOffice endpoints too (the backend's
authorization policy explicitly allows `TenantOwner` onto any Business under their Tenant), so
if a "manage this Business's catalog" deep-link is wanted from within SuperOffice, it can
either link out to the separate BackOffice app (same login token reusable there) or, later,
embed BackOffice's screens directly — a product decision, not a technical blocker.

---

## 2. Tech stack

- **React** (Vite, not Create React App — CRA is unmaintained). TypeScript.
- Routing: React Router.
- Data fetching: TanStack Query (React Query) recommended — handles caching/refetch/loading
  states cleanly against a REST API like this one.
- Forms: any (React Hook Form is a reasonable default) — the API's validation feedback shape
  is documented in §5; server-side DTO validation is fully enforced (see §5), but still build
  client-side validation for responsiveness rather than round-tripping every keystroke.
- Auth token storage: see §4.

---

## 3. Environment configuration

This app is generic — it does **not** need to know which Tenant it serves ahead of time
(unlike BackOffice, which is deployed one-per-Business). Login determines the Tenant; every
API call after that is scoped by the JWT. So the config surface is intentionally tiny:

**`.env` (Vite convention — prefix `VITE_` for anything read in browser code):**

```
VITE_API_BASE_URL=https://api.vastora.app
```

That's it. One value. No client-specific branding needed pre-login (the login screen can be
plain Vastora-branded); once logged in, the Tenant's own name/plan/status is available via
`GET /api/tenants/me` (§6) and can drive the UI chrome from there if desired.

If this app is ever white-labeled per Tenant (unlikely given it's an internal control panel,
but if so), extend this file with a `VITE_TENANT_SLUG` and fetch branding — but there is
currently no public "get Tenant by slug" endpoint (Tenant info is only readable by the
authenticated owner via `/api/tenants/me`), so that would need a new backend endpoint first.

---

## 4. Auth & token strategy

**Login:** `POST /api/auth/login` (the shared BackOffice/SuperOffice/Platform login realm —
works for `TenantOwner`, `PlatformSuperAdmin`, `BusinessAdmin`, `BusinessStaff`,
`DeliveryAgent`; this app should only be used by accounts that come back with
`role: "TenantOwner"` — check the response and reject/redirect otherwise, since a
`BusinessAdmin` logging into SuperOffice would just get 403s from every SuperOffice endpoint).

Every authenticated request needs `Authorization: Bearer <accessToken>`.

**Token lifecycle:**
- Access token expires in ~30 minutes (`accessTokenExpiresAt` in the response — check it
  rather than hardcoding 30).
- Refresh token expires in 30 days, and **rotates on every use** — `POST /api/auth/refresh`
  returns a brand-new refresh token; the old one is immediately revoked. Always store
  whichever refresh token came back from the *last* successful call, never reuse an old one.
- `POST /api/auth/logout` with `{ "refreshToken": "..." }` revokes it server-side.
- **Password reset (added 2026-08-15, main blueprint §9.10):** `POST /api/auth/forgot-password`
  `{ email, redirectBaseUrl? }` always 204s regardless of whether the email matches an account —
  don't build a UI that reveals which. `POST /api/auth/reset-password` `{ token, newPassword }`
  (shared across every realm) resets it and revokes every active session for that user
  server-side — the user will need to log in again after a reset, including on this device.
- **Send `redirectBaseUrl: window.location.origin`, added 2026-08-19.** Without it the emailed
  link falls back to a single platform-wide default, which may not be this deployment's actual
  address. Only honored if it exactly matches your own Tenant's `superOfficeDomain` — **updated
  2026-08-19: this is no longer static backend config.** It's set per-Tenant by Platform, not by
  this app (deliberately: if a TenantOwner is themselves locked out, they can't be the one who
  fixes where their own reset link points). If the reset link isn't landing here, it means Platform
  hasn't set this Tenant's `superOfficeDomain` yet — ask them to, there's nothing this app can do
  about it itself. A non-matching value is silently ignored, not an error — safe to always send it.

**Recommended storage (foundation-phase pragmatic choice):** both tokens in `localStorage`.
This is not the most secure option (an httpOnly-cookie-based flow through a small BFF would
be better against XSS) but there is no BFF in front of this API today — that's backend
hardening work tracked in the main blueprint's roadmap, not something to block this app on.
Revisit if/when that lands.

**Recommended flow:** an API client wrapper that catches `401` responses, attempts one
`POST /api/auth/refresh` using the stored refresh token, retries the original request once on
success, and hard-redirects to `/login` (clearing storage) on failure.

---

## 5. Error handling contract

Every error response is an RFC 7807 `application/problem+json` body:

```json
{
  "status": 404,
  "title": "Business '...' was not found.",
  "type": "https://httpstatuses.io/404",
  "errors": { "fieldName": ["message"] }
}
```

`errors` is only present on `400` validation failures; otherwise just `status`/`title`/`type`.
Status codes in play: `400` (validation), `401` (missing/expired/invalid token), `403`
(authenticated but not allowed — e.g. a `TenantOwner` hitting another Tenant's Business path,
which actually returns `404` instead — see next paragraph), `404` (not found, or a resource
that exists but isn't yours — deliberately indistinguishable), `409` (conflict — e.g. slug
already taken, or trying to add a second Business to a `SingleBusiness` Tenant), `500`
(unexpected).

**Cross-tenant access returns 404, not 403, on purpose.** If a `TenantOwner` tries to fetch a
Business belonging to a different Tenant, the API returns 404 rather than confirming the
Business exists at all. Don't build UI that distinguishes "doesn't exist" from "not yours" —
the API deliberately doesn't let you.

**DTO-level validation is enforced server-side** (empty required strings, malformed emails,
length limits, etc. — a global filter runs the backend's FluentValidation rules on every write
before the action executes; fixed 2026-08-15). Still build client-side validation for
responsiveness, but don't build around the API silently accepting bad input — it won't.
Business rule violations (duplicate slugs, Business-count limits, etc.) are also enforced today
via typed exceptions (409/404).

---

## 6. API reference

Base URL = `VITE_API_BASE_URL`. All paths below are relative to it. All require
`Authorization: Bearer <token>` with `role: "TenantOwner"` unless noted otherwise.

### 6.1 Auth (shared realm — not SuperOffice-specific, but this app needs all of it)

| Method | Path | Auth | Body | Returns |
|---|---|---|---|---|
| POST | `/api/auth/login` | none | `{ email, password }` | `AuthResponse` |
| POST | `/api/auth/refresh` | none | `{ refreshToken }` | `AuthResponse` |
| POST | `/api/auth/logout` | none | `{ refreshToken }` | 204 No Content |
| GET | `/api/auth/me` | any authenticated | — | `UserSummaryResponse` |
| PUT | `/api/auth/me` | any authenticated | `{ fullName, phone }` | `UserSummaryResponse` |

```ts
type AuthResponse = {
  accessToken: string;
  accessTokenExpiresAt: string;   // ISO 8601
  refreshToken: string;
  refreshTokenExpiresAt: string;  // ISO 8601
  user: UserSummaryResponse;
};

type UserSummaryResponse = {
  id: string;
  fullName: string;
  email: string;
  role: "PlatformSuperAdmin" | "TenantOwner" | "BusinessAdmin" | "BusinessStaff" | "DeliveryAgent" | "Customer";
  tenantId: string;   // empty string "" if not applicable to this role
  businessId: string; // empty string "" for TenantOwner (they aren't scoped to one Business)
  status: "PendingVerification" | "Active" | "Blocked";
};
```

### 6.2 Tenant (your own subscription info)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/tenants/me` | — | `TenantResponse` |

```ts
type TenantResponse = {
  id: string;
  name: string;
  slug: string;
  type: "SingleBusiness" | "MultiBusiness";
  status: "PendingSetup" | "Active" | "Suspended" | "Cancelled";
  plan: "Trial" | "Starter" | "Growth" | "Enterprise";
  ownerUserId: string;
  contactEmail: string;
  contactPhone: string;
  superOfficeDomain: string | null; // added 2026-08-19 — read-only here, see note below
  createdAt: string; // ISO 8601
};
```

There is currently **no PATCH endpoint for a Tenant's own profile** (name/contact info) —
only Platform staff can change `Status`/`Plan` (not exposed to this app). If Tenant
self-service profile editing is needed, that's a backend gap to flag, not something to work
around client-side.

**`superOfficeDomain` is read-only through this app, deliberately (added 2026-08-19).** It's the
value §4's `redirectBaseUrl` note above validates against, but the writer
(`PATCH /api/platform/tenants/{tenantId}/superoffice-domain`) is Platform-only, not exposed here —
if a TenantOwner's own account gets locked out, they need someone above them able to fix where
their reset link points, so this deliberately isn't self-service. Show it on the Tenant profile
read-only, and if it's `null` (unset), that's a real "your password-reset link won't work off this
app's own address until Platform sets this" state worth surfacing, not silently ignoring.

**Usage vs. plan limits (added 2026-08-15, main blueprint §9.9):**

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/tenants/me/usage` | — | `TenantUsageResponse` |

```ts
type BusinessUsageEntry = {
  businessId: string;
  businessName: string;
  staffCount: number;
  maxStaffPerBusiness: number | null;   // null = unlimited (Enterprise)
  productCount: number;
  maxProductsPerBusiness: number | null;
};
type TenantUsageResponse = {
  plan: TenantResponse["plan"];
  businessCount: number;
  maxBusinesses: number | null;         // null = unlimited (Enterprise)
  businesses: BusinessUsageEntry[];
};
```

Each plan tier caps Businesses per Tenant, staff per Business, and products per Business
(Trial: 1/3/20, Starter: 1/10/200, Growth: 5/50/2000, Enterprise: unlimited — see the main
blueprint's `SubscriptionPlanLimits` for the authoritative numbers, since they can change
without this doc being updated). Hitting a limit doesn't show up here first — it shows up as a
`409` from the action that would exceed it (`POST .../superoffice/businesses`,
`POST .../businesses/{id}/staff`, `POST .../businesses/{id}/products`), with a message like
`"Your 'Trial' plan allows up to 1 Business(es). Upgrade your plan to add another."` This
endpoint is for *displaying* usage proactively (a "2 of 3 staff used" indicator, disabling the
"Add Business" button at the limit) — build the UI to check it before the user hits the wall,
but still handle the 409 gracefully since usage can go stale between page load and submit.

### 6.3 Businesses (the core of this app)

All under `/api/superoffice/businesses`, always implicitly scoped to your own Tenant — you
never pass a `tenantId`, the backend reads it from your JWT.

| Method | Path | Body | Returns | Notes |
|---|---|---|---|---|
| GET | `/api/superoffice/businesses` | — | `BusinessResponse[]` | Every Business you own |
| POST | `/api/superoffice/businesses` | `CreateBusinessRequest` | `BusinessResponse` | 409 if your Tenant is `SingleBusiness` and already has one |
| GET | `/api/superoffice/businesses/{businessId}` | — | `BusinessResponse` | 404 if not yours |
| PUT | `/api/superoffice/businesses/{businessId}` | `UpdateBusinessRequest` | `BusinessResponse` | Full profile update |
| PATCH | `/api/superoffice/businesses/{businessId}/status` | bare string, e.g. `"Suspended"` | `BusinessResponse` | See note below |
| PATCH | `/api/businesses/{businessId}/delivery-module` | `{ enabled: boolean }` | `BusinessResponse` | Not under `/superoffice/` — it's the shared BackOffice route (main blueprint §9.14), but a `TenantOwner`'s JWT works on it too, same as any BackOffice endpoint (§1's note above on reusing the token). Turns the DeliveryAgent workflow on/off for one Business; existing agents and in-flight assignments aren't touched, only new creation/assignment is blocked while off. |
| GET | `/api/superoffice/businesses/{businessId}/mail-settings` | — | `BusinessMailSettingsResponse` | Added 2026-08-19, main blueprint §9.10 |
| PUT | `/api/superoffice/businesses/{businessId}/mail-settings` | `UpdateBusinessMailSettingsRequest` | `BusinessMailSettingsResponse` | Added 2026-08-19 — see note below |
| PATCH | `/api/superoffice/businesses/{businessId}/domains` | `UpdateBusinessDomainsRequest` | `BusinessResponse` | Added 2026-08-19, reshaped same day — sets `shopDomain` and `backOfficeDomain`; see note below |

```ts
type BusinessResponse = {
  id: string;
  tenantId: string;
  name: string;
  slug: string;              // public, globally unique — used in Shop URLs
  shopDomain: string | null;       // renamed from customDomain 2026-08-19 — see domain note below
  backOfficeDomain: string | null; // added 2026-08-19 — see domain note below
  description: string;
  logoUrl: string;
  bannerUrl: string;
  themeColor: string;        // hex, e.g. "#111827"
  currency: string;          // e.g. "USD"
  contactEmail: string;
  contactPhone: string;
  status: "Draft" | "Active" | "Suspended";
  deliveryModuleEnabled: boolean;  // added 2026-08-15 — see the delivery-module row above
  defaultDeliveryFee: number;      // added 2026-08-15, main blueprint §9.7 — flat fee Shop checkout falls back to when the customer's request omits one
  createdAt: string;
};

type CreateBusinessRequest = {
  name: string;
  slug?: string | null;      // omit/null to auto-generate from name
  description: string;
  contactEmail: string;
  contactPhone: string;
  currency?: string;         // defaults to "USD" server-side if omitted
};

type UpdateBusinessRequest = {
  name: string;
  description: string;
  logoUrl: string;
  bannerUrl: string;
  themeColor: string;
  contactEmail: string;
  contactPhone: string;
  currency: string;
  defaultDeliveryFee: number;  // added 2026-08-15, main blueprint §9.7
};
```

**The status PATCH body is a bare JSON string, not an object** — send `"Active"` (with
quotes, as the raw request body / `JSON.stringify("Active")`), not `{"status": "Active"}`.
This is true of every `PATCH .../status` endpoint across the whole Vastora API — a backend
inconsistency worth knowing about, not a typo in this doc.

**Gating the "Add Business" button:** two independent caps can 409 this action, check both.
First, `TenantResponse.type` (from §6.2) — if `"SingleBusiness"`, either hide the create action
entirely or expect a 409 when the Tenant already has one Business (always true for a
`SingleBusiness` Tenant after their first Business exists). There is still no self-serve
upgrade path a `TenantOwner` can trigger themselves — a Platform admin now has an endpoint to
flip the type (`PATCH /api/platform/tenants/{id}/type`, main blueprint §9.4, added 2026-08-15),
but it isn't exposed to this app and there's no request/approve workflow yet (still a product
decision) — surface a "contact support to upgrade" message rather than a broken create flow.
Second, even a `MultiBusiness` Tenant is capped by its plan's `maxBusinesses` (§6.2's usage
endpoint) — at Trial that's 1, same ceiling as a `SingleBusiness` Tenant, so don't assume
`type: "MultiBusiness"` alone means the button is always safe to show enabled.

**Mail domain (added 2026-08-19, main blueprint §9.10).** This is deliberately SuperOffice-only
— BackOffice has no route to it at all, not even for `BusinessAdmin`. Every email addressed to
this Business's world (customers, `BusinessAdmin`/`BusinessStaff`, `DeliveryAgent`) sends
through here once `enabled` and `host` are set; until then it silently falls back to the
platform's own SMTP account, so leaving this unconfigured is a safe default, not a broken one.

```ts
type BusinessMailSettingsResponse = {
  enabled: boolean;
  host: string;
  port: number;
  username: string;
  hasPassword: boolean;    // true if a credential is on file — the password itself is never returned
  fromAddress: string;
  fromName: string;
};

type UpdateBusinessMailSettingsRequest = {
  enabled: boolean;
  host: string;
  port: number;
  username: string;
  password: string | null; // omit or send null/"" to leave the stored password unchanged
  fromAddress: string;
  fromName: string;
};

type UpdateBusinessDomainsRequest = {
  shopDomain: string | null;       // e.g. "shop.antivaly.com" — null/"" clears it
  backOfficeDomain: string | null; // e.g. "staff.antivaly.com" — null/"" clears it
};
```

**Domains (added 2026-08-19, reshaped same day — main blueprint §9.10).** `customDomain` existed
on `BusinessResponse` since the foundation session with no writer anywhere; this PATCH was
originally its writer, then split into two fields the same day once it became clear one field
couldn't serve both purposes it was being asked to. Both are SuperOffice-only, same as
mail-settings, and this is the actual dynamic-domain-management form: no config file, no
redeploy — a TenantOwner sets these here and the change takes effect on the next request.

- **`shopDomain`** — this Business's customer-facing Shop domain. Used server-side to (1) resolve
  a relative `logoUrl`/`bannerUrl` to an absolute URL for outbound email, preferred over the
  platform's own base URL when set, and (2) validate a Customer's `redirectBaseUrl` on the Shop's
  own `register`/`forgot-password` calls (see the Antivaly Shop blueprint).
- **`backOfficeDomain`** — the address this Business's own `BusinessAdmin`/`BusinessStaff`/
  `DeliveryAgent` staff sign in at. Validates *their* `redirectBaseUrl` on `forgot-password` (see
  the BackOffice blueprint) — deliberately a separate field from `shopDomain`, since a Business's
  public storefront and its staff console are not usually the same address, and a customer domain
  must never double as a valid staff-reset target.

Neither is yet a routing/DNS/TLS feature — pointing an actual domain's DNS at Vastora and
terminating TLS for it is still open infrastructure work (see main blueprint's Content/domain
roadmap notes), so don't build a "your domain is live" UI around either field yet. Safe default:
leave both unset for a Business testing locally — email/redirect validation then falls back to
whatever the platform's own configured/request-derived defaults resolve to.

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/superoffice/analytics` | — | `TenantAnalyticsResponse` |

```ts
type BusinessAnalyticsEntry = { businessId: string; businessName: string; orderCount: number; revenue: number };
type TopProductStat = { productId: string; productName: string; quantitySold: number; revenue: number };
type TenantAnalyticsResponse = {
  totalRevenue: number;
  totalOrders: number;
  businesses: BusinessAnalyticsEntry[];
  topProducts: TopProductStat[];  // top 10 by quantity sold
};
```

**`revenue` is recognized on delivered orders only** — an order sitting in `Processing` never
counts toward it, even though it does count toward `orderCount` (every non-cancelled order
counts there, overall and per Business). This is deliberate and matches how the backend's own
accounting module (main blueprint §9.16) recognizes revenue, so this number and a Business's
own P&L report (BackOffice, not this app) should never disagree. No date-range filter on this
endpoint — it's all-time; if a windowed view is needed, that's a backend gap to request, not
something to fake by filtering client-side against an unbounded order list this endpoint
doesn't provide.

**Two caveats added 2026-08-16.** First, BackOffice now has its own *windowed* per-business
dashboard (`GET /api/businesses/{id}/analytics/dashboard`, main blueprint §9.32) with revenue,
gross profit, AOV and a daily series. This endpoint stays the cross-business rollup; if a
TenantOwner wants a date range for one Business, that dashboard is the answer and this app can
link to it rather than reimplementing it.

Second, and worth knowing before you render a currency symbol: **`totalRevenue` sums raw
decimals across Businesses regardless of their individual `currency`.** For a Tenant whose
Businesses all trade in one currency that's correct; for a mixed-currency Tenant the total is
meaningless. Main blueprint §9.31/§9.38 record this as open — no FX conversion exists. Render
per-Business figures with each Business's own currency, and either suppress the grand total or
label it plainly when the Tenant's Businesses don't agree on one.

### 6.5 Integrations — webhooks & API keys (added 2026-08-16, main blueprint §9.39)

`TenantOwner` only, and deliberately: these credentials span **every** Business the Tenant owns,
which is why they live here rather than in a single BackOffice.

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/integrations/webhooks/events` | — | `string[]` — the subscribable event names |
| GET | `/api/integrations/webhooks?page=&pageSize=` | — | `PagedResult<WebhookResponse>` |
| POST | `/api/integrations/webhooks` | `CreateWebhookRequest` | `WebhookResponse` — **carries the secret, once** |
| DELETE | `/api/integrations/webhooks/{webhookId}` | — | 204 |
| GET | `/api/integrations/webhooks/{webhookId}/deliveries?page=&pageSize=` | — | `PagedResult<WebhookDeliveryResponse>` |
| GET | `/api/integrations/api-keys?page=&pageSize=` | — | `PagedResult<ApiKeyResponse>` |
| POST | `/api/integrations/api-keys` | `CreateApiKeyRequest` | `ApiKeyResponse` — **carries the key, once** |
| DELETE | `/api/integrations/api-keys/{apiKeyId}` | — | 204 (revoke) |

```ts
type CreateWebhookRequest = {
  url: string;            // must be absolute HTTPS — plain HTTP is rejected with a 409
  events: string[];       // from /webhooks/events; an unknown name is a 409
  description: string;
  businessId: string;     // "" = tenant-wide, every Business the Tenant owns
};
type WebhookResponse = {
  id: string; url: string; events: string[]; description: string; businessId: string;
  isActive: boolean;
  secret: string | null;  // NON-NULL ONLY on the create response. Stored hashed; unrecoverable.
  lastDeliveryAt: string | null;
  consecutiveFailures: number;
  disabledAt: string | null;   // auto-disabled after 10 consecutive failures
  createdAt: string;
};
type WebhookDeliveryResponse = {
  id: string; eventName: string; responseStatusCode: number | null;
  error: string | null; attemptCount: number; succeeded: boolean; createdAt: string;
};
type CreateApiKeyRequest = {
  name: string;
  businessId: string | null;   // null/"" = spans every Business under the Tenant
  scopes: ("read" | "write")[] | null;   // defaults to ["read"]
  expiresAt: string | null;
};
type ApiKeyResponse = {
  id: string; name: string; keyId: string;
  secret: string | null;   // NON-NULL ONLY on the create response — the full "keyId.secret" credential
  businessId: string; scopes: string[];
  expiresAt: string | null; lastUsedAt: string | null; revokedAt: string | null; createdAt: string;
};
```

**Event names** (also served by `/webhooks/events`, prefer that over hardcoding):
`order.created`, `order.status_changed`, `order.delivered`, `order.picked_up` (added 2026-08-18,
§9.47 — the Pickup-order equivalent of `order.delivered`; fired instead of it, never alongside
it, when a Pickup order reaches `PickedUp`), `product.low_stock`, `return.requested`,
`review.submitted`.

Things to build around:

- **The secret and the API key are shown exactly once**, in the create response. Present them in
  a copy-to-clipboard dialog that says so plainly, and offer "delete and recreate" as the
  rotation path — there is no reveal endpoint and there never will be.
- **Payloads are signed** with HMAC-SHA256 in an `X-Vastora-Signature` header
  (`sha256=<hex>`), alongside `X-Vastora-Event` and `X-Vastora-Delivery`. Document that in the
  UI next to the URL field; a receiver that doesn't verify the signature has an open endpoint.
- **Endpoints auto-disable after 10 consecutive failures.** Surface `consecutiveFailures` and
  `disabledAt` prominently — a silently dead integration is the failure mode here — and make
  the deliveries log (with status codes and errors) easy to reach from the subscription row.
- **API keys are issued and validated but not yet accepted as request authentication.** The
  backend can create and verify them; no authentication handler consumes them on a live request
  yet (main blueprint §9.39). Build the management screen, but don't promise customers that a
  key will authenticate an API call today.

---

## 7. Recommended pages

1. **Login** — `POST /api/auth/login`; on success, verify `role === "TenantOwner"` before
   proceeding (redirect elsewhere / show an error otherwise).
2. **Dashboard** — list of Businesses (§6.3 GET all) as cards/table: name, slug, status,
   currency, plus §6.4's `GET /api/superoffice/analytics` for a total-revenue/total-orders
   summary and a top-products list. Per-Business `revenue`/`orderCount` from that same
   response can annotate each card without a second call per Business.
3. **Tenant profile** (read-only) — `GET /api/tenants/me`: name, slug, type, status, plan.
4. **Business detail / edit** — `GET` + `PUT` one Business, `PATCH` its status
   (Active/Suspended toggle — Draft is presumably only ever set at creation, not toggled back
   to from the UI).
5. **Create Business** — form → `POST`, gated per the note in §6.3.
6. **My profile** — `GET`/`PUT /api/auth/me`.
7. **Integrations** (§6.5) — webhook subscriptions with a health indicator off
   `consecutiveFailures`/`disabledAt`, a per-subscription delivery log, and API-key management.
   Both create dialogs must show the one-time secret clearly.

---

## 8. Notes for whoever implements this

- **Pagination now exists, and this is a breaking change (2026-08-16, main blueprint §9.18).**
  The gap this bullet used to flag is closed: list endpoints return a
  `PagedResult<T>` envelope — `{ items, page, pageSize, totalCount, totalPages, hasNextPage,
  hasPreviousPage }` — and accept `?page=&pageSize=` (default 25, max 200). Read `.items`.
  `GET /api/superoffice/businesses` is the one list in this app small enough that a Tenant will
  rarely page it, but the shape changed regardless.
- `Draft` business status exists in the enum but nothing in the current API transitions a
  Business into it automatically — it's presumably meant for "created but not yet ready to
  go live," worth clarifying the intended lifecycle with product before building status-change
  UI around it.
- No image upload endpoint exists yet — `logoUrl`/`bannerUrl` are plain string fields the
  caller supplies (host images elsewhere, paste the URL).
