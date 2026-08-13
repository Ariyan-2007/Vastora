# SuperOffice Frontend Blueprint

**Self-contained.** This file assumes no other context — move it into the SuperOffice
project's own repo and hand it to a fresh session; everything needed to build against the
Vastora API is here. If the Vastora API changes, this doc must be updated in the same session
as the change (see the main `VASTORA_BLUEPRINT.md`, §10, in the Vastora backend repo).

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
  is documented in §5, build client-side validation to match since the API's own server-side
  DTO validation is not fully wired up yet (see §5's caveat).
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

**Known gap:** DTO-level validation (empty required strings, malformed values) is not yet
enforced server-side for every endpoint — the backend's FluentValidation validators exist but
aren't fully wired into every controller action yet. Build defensive client-side validation
(required fields, sane lengths) rather than relying on the API to catch bad input; business
rule violations (duplicate slugs, Business-count limits, etc.) *are* enforced server-side today
via typed exceptions (409/404), just not raw shape validation.

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
  createdAt: string; // ISO 8601
};
```

There is currently **no PATCH endpoint for a Tenant's own profile** (name/contact info) —
only Platform staff can change `Status`/`Plan` (not exposed to this app). If Tenant
self-service profile editing is needed, that's a backend gap to flag, not something to work
around client-side.

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

```ts
type BusinessResponse = {
  id: string;
  tenantId: string;
  name: string;
  slug: string;              // public, globally unique — used in Shop URLs
  customDomain: string | null;
  description: string;
  logoUrl: string;
  bannerUrl: string;
  themeColor: string;        // hex, e.g. "#111827"
  currency: string;          // e.g. "USD"
  contactEmail: string;
  contactPhone: string;
  status: "Draft" | "Active" | "Suspended";
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
};
```

**The status PATCH body is a bare JSON string, not an object** — send `"Active"` (with
quotes, as the raw request body / `JSON.stringify("Active")`), not `{"status": "Active"}`.
This is true of every `PATCH .../status` endpoint across the whole Vastora API — a backend
inconsistency worth knowing about, not a typo in this doc.

**Gating the "Add Business" button:** check `TenantResponse.type` (from §6.2) — if
`"SingleBusiness"`, either hide the create action entirely or expect a 409 when the Tenant
already has one Business (which, for a `SingleBusiness` Tenant, is always true after their
first Business exists). There is no self-serve upgrade path from `SingleBusiness` to
`MultiBusiness` yet (Platform staff only, and not even exposed via an endpoint yet — see the
main blueprint's roadmap §9.4) — surface a "contact support to upgrade" message rather than a
broken create flow.

---

## 7. Recommended pages

1. **Login** — `POST /api/auth/login`; on success, verify `role === "TenantOwner"` before
   proceeding (redirect elsewhere / show an error otherwise).
2. **Dashboard** — list of Businesses (§6.3 GET all) as cards/table: name, slug, status,
   currency. No aggregate revenue/order stats endpoint exists yet (main blueprint roadmap
   §9.8) — don't build a stats widget that has nothing to call; a plain list is the honest
   MVP here.
3. **Tenant profile** (read-only) — `GET /api/tenants/me`: name, slug, type, status, plan.
4. **Business detail / edit** — `GET` + `PUT` one Business, `PATCH` its status
   (Active/Suspended toggle — Draft is presumably only ever set at creation, not toggled back
   to from the UI).
5. **Create Business** — form → `POST`, gated per the note in §6.3.
6. **My profile** — `GET`/`PUT /api/auth/me`.

---

## 8. Notes for whoever implements this

- Every list endpoint here returns a plain array, no pagination — fine at foundation scale,
  revisit if a Tenant ever owns dozens of Businesses.
- `Draft` business status exists in the enum but nothing in the current API transitions a
  Business into it automatically — it's presumably meant for "created but not yet ready to
  go live," worth clarifying the intended lifecycle with product before building status-change
  UI around it.
- No image upload endpoint exists yet — `logoUrl`/`bannerUrl` are plain string fields the
  caller supplies (host images elsewhere, paste the URL).
