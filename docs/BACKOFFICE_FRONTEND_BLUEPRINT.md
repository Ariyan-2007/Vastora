# BackOffice Frontend Blueprint

**Self-contained.** This file assumes no other context — move it into the BackOffice
project's own repo and hand it to a fresh session; everything needed to build against the
Vastora API is here. If the Vastora API changes, this doc must be updated in the same session
as the change (see the intro note at the top of the main `VASTORA_BLUEPRINT.md` in the Vastora
backend repo).

---

## 1. What this app is

**BackOffice** is the day-to-day operations console for **one Business** on Vastora: its
catalog (categories, products), coupons, staff, customers, delivery agents, and orders. It's
used by `BusinessAdmin`, `BusinessStaff`, and `DeliveryAgent` accounts, all scoped to that one
Business.

### Where it sits in the platform

Vastora is a multi-tenant e-commerce API platform sold as a subscription. Three tiers:

```
Platform (Vastora itself — not this app)
  └─ Tenant (the paying subscriber — owns one or more Businesses)
       └─ Business (one storefront: Landing Page + Shop + THIS APP is its BackOffice)
```

**Every Business gets its own deployment of this app**, pointed at that one Business via
config (see §3). A Tenant that owns several Businesses (a `MultiBusiness` subscription) gets
one BackOffice deployment per Business, plus a separate **SuperOffice** app spanning all of
them (different app, different repo — see `SUPEROFFICE_FRONTEND_BLUEPRINT.md`). A
`TenantOwner`'s or Vastora `PlatformSuperAdmin`'s login also works here (the backend explicitly
allows both onto any Business they're entitled to) — useful for support/oversight, but this
app's primary users are the Business's own `BusinessAdmin`/`BusinessStaff`/`DeliveryAgent`
accounts.

---

## 2. Tech stack

- **React** (Vite SPA, TypeScript). Routing via React Router.
- Data fetching: TanStack Query recommended.
- Auth token storage: see §5.

---

## 3. Environment configuration — **this is the "ship to a new client" file**

This app is deployed **once per Business**. Shipping it to a new client should require
filling in this config and deploying — no code changes.

**`.env` (Vite convention — prefix `VITE_`):**

```
VITE_API_BASE_URL=https://api.vastora.app
VITE_BUSINESS_SLUG=ctg-electronics-shop
```

Two values:

- `VITE_API_BASE_URL` — the Vastora backend this deployment talks to.
- `VITE_BUSINESS_SLUG` — **the one thing that makes this deployment "belong" to a specific
  client.** It's the public, human-readable slug assigned to their Business at signup
  (returned as `business.slug` from the Vastora tenant sign-up call — get it from whoever ran
  onboarding, or from Vastora Platform/SuperOffice staff). Everything else this app needs
  (the Business's real database ID, branding, name) is resolved **at runtime** from that slug
  via the public storefront endpoint — see below — so the config file itself stays tiny and
  never needs a database ID pasted into it by hand.

### How the app should resolve its Business on startup

Before rendering the login screen, call the **public, unauthenticated** endpoint:

```
GET {VITE_API_BASE_URL}/api/shop/{VITE_BUSINESS_SLUG}
→ BusinessResponse   (see §6.2 for the shape)
```

Store `businessResponse.id` (the real Business ID used in every other API path below) and use
`businessResponse.name` / `logoUrl` / `themeColor` / `bannerUrl` to brand the login page and
app chrome — so a freshly-deployed BackOffice already looks like *that* client's app before
anyone even logs in, with zero extra config. If this call 404s, the slug is wrong or the
Business isn't `Active` yet — show a clear "this app isn't configured correctly" screen rather
than a blank page, since that's a deployment misconfiguration, not a user error.

After login (§5), defensively check: if the logged-in user's `businessId` claim is non-empty
(true for `BusinessAdmin`/`BusinessStaff`/`DeliveryAgent`) and doesn't match the resolved
Business ID, show "this account doesn't belong to this shop" and refuse to proceed — the API
would reject every subsequent call with a 403 anyway (see §7), but a clear message beats a
wall of failed requests. `TenantOwner`/`PlatformSuperAdmin` accounts have an empty `businessId`
claim and aren't subject to this check (they're allowed onto any Business they're entitled to
— the API enforces that server-side).

---

## 4. Role-based navigation

Not every logged-in role should see every screen. The API itself enforces all of this
server-side (wrong-role calls get 403), but the UI should match rather than showing dead ends:

| Screen | BusinessAdmin | BusinessStaff | DeliveryAgent | TenantOwner / PlatformSuperAdmin |
|---|:-:|:-:|:-:|:-:|
| Business profile (view) | ✅ | ✅ | — | ✅ |
| Business profile (edit) | ✅ | — | — | ✅ |
| Categories (view/create/edit) | ✅ | ✅ | — | ✅ |
| Categories (delete) | ✅ | — | — | ✅ |
| Products (view/create/edit, image upload) | ✅ | ✅ | — | ✅ |
| Products (delete) | ✅ | — | — | ✅ |
| Coupons (view) | ✅ | ✅ | — | ✅ |
| Coupons (create/edit/delete) | ✅ | — | — | ✅ |
| Inventory (movements, low-stock, valuation, adjustments) | ✅ | ✅ | — | ✅ |
| Expenses / P&amp;L / balance sheet | ✅ | — | — | ✅ |
| Staff (list/create/status) | ✅ | — | — | ✅ |
| Customers (read-only list) | ✅ | — | — | ✅ |
| Delivery agents (list/set any status) | ✅ | ✅ | — | ✅ |
| My delivery status (self) | — | — | ✅ | — |
| Orders (list all / detail / status / assign / payment status) | ✅ | ✅ | — | ✅ |
| My assigned deliveries (self) | — | — | ✅ | — |
| Order status update | ✅ | ✅ | ✅ (their own assigned orders, same endpoint) | ✅ |

**`BusinessAdmin` and `BusinessStaff` are now genuinely split (added 2026-08-15, main blueprint
§9.3).** Staff keeps full day-to-day catalog/order work but loses destructive and
revenue-sensitive actions: category/product delete, and all of coupon create/edit/delete
(coupons directly control discounts, so Staff gets read-only there). The new Inventory
screens (§7.9) follow the catalog split — Staff can adjust stock and view reports, since that's
operational, not destructive. The new Accounting screens (§7.10) are Admin-tier only — financial
records are treated at least as sensitively as coupons. Build the nav to match the table above;
a wrong-role call now genuinely 403s where it wouldn't have before this session.

**`DeliveryAgent` gets a completely different, minimal shell** — no catalog/staff/coupon
screens exist for them at all (the API 403s them). Consider routing DeliveryAgent logins to an
entirely separate simplified layout: "My Deliveries" + "My Status" + profile.

---

## 5. Auth & token strategy

**Login:** `POST /api/auth/login` (shared realm — works for `BusinessAdmin`, `BusinessStaff`,
`DeliveryAgent`, `TenantOwner`, `PlatformSuperAdmin`; reject/redirect any other `role` value
that somehow comes back, and apply the Business-match check from §3).

- Access token ~30 min (`accessTokenExpiresAt` — read it, don't hardcode).
- Refresh token 30 days, **rotates on every refresh** — always persist the newest one.
- `POST /api/auth/refresh` with `{ refreshToken }` → new `AuthResponse`.
- `POST /api/auth/logout` with `{ refreshToken }` → revokes it, 204.
- `POST /api/auth/forgot-password` `{ email }` / `POST /api/auth/reset-password`
  `{ token, newPassword }` (added 2026-08-15, main blueprint §9.10) — always 204s regardless of
  whether the email matches an account, and a successful reset revokes every active session for
  that user (including the device that requested it). No real email delivery yet — the reset
  token is currently only visible in the backend's server log, so this flow isn't usable by a
  real end user until an email/SMS provider is wired in.

Storage: `localStorage` for both tokens (foundation-phase pragmatic choice — see the
SuperOffice blueprint §4 for the same caveat, it applies identically here). Wrap the API
client to catch 401, refresh once, retry once, else redirect to `/login`.

---

## 6. Error handling contract

Same across all of Vastora — RFC 7807 `application/problem+json`:

```json
{ "status": 404, "title": "...", "type": "https://httpstatuses.io/404", "errors": { "field": ["msg"] } }
```

`errors` only on `400`. Codes: `400` validation, `401` bad/missing/expired token, `403`
authenticated but not entitled (e.g. `BusinessStaff` hitting `PUT` on the Business profile),
`404` not found *or* not yours (cross-tenant/cross-business access deliberately returns 404,
not 403, so it can't be used to probe whether a resource exists), `409` conflict (duplicate
slug/SKU/coupon code, stock too low at checkout — though checkout itself is the Shop app's
concern, not this one), `500` unexpected.

**Raw DTO shape validation (empty strings, malformed values) is enforced server-side** — a
global filter runs the backend's FluentValidation rules on every write before the action
executes (fixed 2026-08-15). Still validate client-side for responsiveness. Business-rule
violations (duplicate codes, etc.) are also enforced today via typed exceptions.

**Every `PATCH .../status` endpoint takes a bare JSON string as its body**, not an object —
e.g. `PATCH .../products/{id}/status` body is literally `"Active"` (quoted string), not
`{"status": "Active"}`. This is consistent across the whole API, not a mistake.

---

## 7. API reference

Base URL = `VITE_API_BASE_URL`. `{businessId}` = the ID resolved in §3 (**not** the slug).
All endpoints below require `Authorization: Bearer <token>` and the caller must be entitled to
that `businessId` (own-Business for `BusinessAdmin`/`BusinessStaff`/`DeliveryAgent`, any
Business under their Tenant for `TenantOwner`, any Business at all for `PlatformSuperAdmin`).

> ### ⚠ Breaking change — 2026-08-16 (main blueprint §9.18)
>
> **Every list endpoint in this document now returns a paged envelope, not a bare array.**
>
> ```ts
> type PagedResult<T> = {
>   items: T[];
>   page: number; pageSize: number; totalCount: number; totalPages: number;
>   hasNextPage: boolean; hasPreviousPage: boolean;
> };
> ```
>
> All of them accept `?page=&pageSize=` (default 25, max 200). Read `.items`. Build a shared
> fetch wrapper that unwraps the envelope and feeds a pager — before this the API returned every
> order a business had ever taken in a single response, and the tables in this app were sorting
> and slicing that client-side.
>
> Server-side filtering came with it, so stop filtering in the browser: orders take
> `status`, `paymentStatus`, `search`, `from`, `to`; products take `search`.

**§9B added 55 endpoints across nine new BackOffice areas** — returns, reviews, promotions,
customer groups, gift cards, store credit, shipping zones, storefront content, an audit log, a
real dashboard and CSV import/export. They are documented in §7.11–§7.16 below.

### 7.1 Auth (see also §5)

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `/api/auth/login` | `{ email, password }` | `AuthResponse` |
| POST | `/api/auth/refresh` | `{ refreshToken }` | `AuthResponse` |
| POST | `/api/auth/logout` | `{ refreshToken }` | 204 |
| POST | `/api/auth/forgot-password` | `{ email }` | 204 |
| POST | `/api/auth/reset-password` | `{ token, newPassword }` | 204 |
| GET | `/api/auth/me` | — | `UserSummaryResponse` |
| PUT | `/api/auth/me` | `{ fullName, phone }` | `UserSummaryResponse` |

```ts
type AuthResponse = {
  accessToken: string; accessTokenExpiresAt: string;
  refreshToken: string; refreshTokenExpiresAt: string;
  user: UserSummaryResponse;
};
type UserSummaryResponse = {
  id: string; fullName: string; email: string;
  role: "PlatformSuperAdmin" | "TenantOwner" | "BusinessAdmin" | "BusinessStaff" | "DeliveryAgent" | "Customer";
  tenantId: string; businessId: string;
  status: "PendingVerification" | "Active" | "Blocked";
};
```

### 7.2 Business profile

| Method | Path | Roles | Body | Returns |
|---|---|---|---|---|
| GET | `/api/businesses/{businessId}` | Admin, Staff, TenantOwner, Platform | — | `BusinessResponse` |
| PUT | `/api/businesses/{businessId}` | Admin, TenantOwner, Platform (**not** Staff) | `UpdateBusinessRequest` | `BusinessResponse` |
| PATCH | `/api/businesses/{businessId}/delivery-module` | Admin, TenantOwner, Platform (**not** Staff) | `{ enabled: boolean }` | `BusinessResponse` |

Added 2026-08-15 (main blueprint §9.14): the last row turns the DeliveryAgent workflow on/off
for this Business — pickup-only sellers or ones using a third-party courier can switch it off.
While off, `POST .../staff` with `role: "DeliveryAgent"` 409s ("Delivery module is disabled for
this business.") and `PATCH .../orders/{orderId}/assign-delivery` 409s the same way — build the
BackOffice UI to hide/disable the "add delivery agent" and "assign delivery" actions when
`business.deliveryModuleEnabled` is `false`, rather than letting the user hit the 409. Existing
`DeliveryAgent` staff and any order already assigned to one are untouched by the toggle — it
only blocks *new* creation/assignment.

**Plan usage (added 2026-08-15, main blueprint §9.9):** `GET /api/tenants/me/usage` returns
this Business's staff/product counts against its plan's limits (`TenantUsageResponse`, shape in
the SuperOffice blueprint §6.2) — but it's `TenantOwner`-only, so most BackOffice sessions
(`BusinessAdmin`/`BusinessStaff`) can't call it. See §7.4 and §7.6 for how the two limits that
actually bite in BackOffice (products, staff) show up as 409s instead.

```ts
type BusinessResponse = {
  id: string; tenantId: string; name: string; slug: string; customDomain: string | null;
  description: string; logoUrl: string; bannerUrl: string; themeColor: string;
  currency: string; contactEmail: string; contactPhone: string;
  status: "Draft" | "Active" | "Suspended";
  deliveryModuleEnabled: boolean;  // added 2026-08-15
  defaultDeliveryFee: number;      // added 2026-08-15, §9.7 — now the FALLBACK when no §7.13 shipping zone matches
  createdAt: string;
  // --- added 2026-08-16 (§9B) ---
  tax: TaxSettings;
  returnWindowDays: number;
  reviewsEnabled: boolean;
  guestCheckoutEnabled: boolean;
};

type TaxSettings = {
  enabled: boolean;
  defaultRatePercent: number;      // e.g. 15 for 15%
  pricesIncludeTax: boolean;       // TRUE = catalog prices already contain tax (the VAT convention)
  taxShipping: boolean;
  classRates: Record<string, number>;  // Product.taxClass -> rate; unlisted classes use the default
  registrationNumber: string;      // VAT/GST/BIN, printed on invoices
  displayName: string;             // "VAT" | "GST" | "Sales Tax"
};
type InvoiceSettings = {
  numberPrefix: string;            // e.g. "INV-"
  lastNumber: number;              // READ-ONLY in practice — see note
  legalName: string; legalAddress: string; registrationNumber: string; footerNote: string;
};

// Every §9B field is OPTIONAL. Omitting a section leaves it as it was — this is a patch, not a
// replace — so the pre-2026-08-16 request body still works and a partial save can't wipe tax config.
type UpdateBusinessRequest = {
  name: string; description: string; logoUrl: string; bannerUrl: string;
  themeColor: string; contactEmail: string; contactPhone: string; currency: string;
  defaultDeliveryFee: number;  // added 2026-08-15, §9.7
  tax?: TaxSettings | null;
  invoicing?: InvoiceSettings | null;
  returnWindowDays?: number | null;      // 0 disables returns entirely
  reviewsEnabled?: boolean | null;
  autoPublishReviews?: boolean | null;   // write-only; not echoed on BusinessResponse
  guestCheckoutEnabled?: boolean | null;
};
```

**`invoicing.lastNumber` is ignored on write.** It's the sequential invoice counter (§9.33) and
the server preserves its own value whatever you send — rewinding it would issue duplicate
invoice numbers on a legally sequential document. Show it read-only, if at all.

**`pricesIncludeTax` is the field most worth getting right.** True means catalog prices already
contain tax and it is *extracted* at checkout; false means it's added on top. Getting it
backwards silently overcharges (or undercharges) every customer, so label the toggle in plain
language — "My prices already include tax" — rather than with the field name.

### 7.3 Categories — `/api/businesses/{businessId}/categories`

| Method | Path | Roles | Body | Returns |
|---|---|---|---|---|
| GET | `` | any BackOffice role | — | `CategoryResponse[]` (flat) |
| GET | `/tree` | any BackOffice role | — | `CategoryTreeNode[]` (nested, added 2026-08-15, §9.5) |
| GET | `/{categoryId}` | any BackOffice role | — | `CategoryResponse` |
| POST | `` | any BackOffice role | `CreateCategoryRequest` | `CategoryResponse` |
| PUT | `/{categoryId}` | any BackOffice role | `UpdateCategoryRequest` | `CategoryResponse` |
| DELETE | `/{categoryId}` | Admin, TenantOwner, Platform (**not** Staff, added 2026-08-15, §9.3) | — | 204 |

```ts
type CategoryResponse = {
  id: string; businessId: string; name: string; slug: string;
  parentCategoryId: string | null; description: string; imageUrl: string;
  sortOrder: number; isActive: boolean;
};
type CategoryTreeNode = {
  id: string; businessId: string; name: string; slug: string;
  description: string; imageUrl: string; sortOrder: number; isActive: boolean;
  children: CategoryTreeNode[];
};
type CreateCategoryRequest = {
  name: string; slug?: string | null; parentCategoryId?: string | null;
  description: string; imageUrl: string; sortOrder: number;
};
type UpdateCategoryRequest = {
  name: string; parentCategoryId: string | null;
  description: string; imageUrl: string; sortOrder: number; isActive: boolean;
};
```

**Nested trees are now server-provided** — `GET .../categories/tree` returns the same
Categories nested by `parentCategoryId` instead of flat, so a category-management UI with
expand/collapse or a nav dropdown no longer needs to build the tree client-side. The flat `GET`
still exists and is still the right choice for a simple edit-in-a-table view; use whichever
shape suits the screen.

**Categories can be re-parented after creation (added 2026-08-17).** `PUT .../categories/{id}`
now takes `parentCategoryId` too — send `null` to promote a category to top-level, or another
Category's id to move it under a new parent. A parent-picker on the edit form should exclude the
category being edited and any of its own descendants; the server rejects both (400, field
`parentCategoryId`) since either would turn the tree into a cycle that the `/tree` endpoint's
recursive walk can't terminate on. If you don't want re-parenting on a given screen, just echo
back the category's current `parentCategoryId` unchanged.

### 7.4 Products — `/api/businesses/{businessId}/products`

| Method | Path | Roles | Body | Returns |
|---|---|---|---|---|
| GET | `` | any BackOffice role | — | `ProductResponse[]` (every status, incl. Draft/Archived — this is the BackOffice view) |
| GET | `/{productId}` | any BackOffice role | — | `ProductResponse` |
| POST | `` | any BackOffice role | `CreateProductRequest` | `ProductResponse` (created as `Draft`) |
| PUT | `/{productId}` | any BackOffice role | `UpdateProductRequest` | `ProductResponse` |
| PATCH | `/{productId}/status` | any BackOffice role | bare string, e.g. `"Active"` | `ProductResponse` |
| POST | `/{productId}/images` | any BackOffice role | `multipart/form-data`, field `file` | `ProductResponse` (added 2026-08-15, §9.5) |
| DELETE | `/{productId}` | Admin, TenantOwner, Platform (**not** Staff, added 2026-08-15, §9.3) | — | 204 |

```ts
type ProductVariantResponse = {
  id: string; attributeSummary: string; sku: string; priceOverride: number | null; stockQuantity: number;
};
type ProductVariantRequest = {
  id?: string | null;  // omit/null when creating a new variant — the server generates one
  attributeSummary: string; sku: string; priceOverride: number | null; stockQuantity: number;
};
type ProductResponse = {
  id: string; businessId: string; categoryId: string; name: string; slug: string; sku: string;
  description: string; price: number; compareAtPrice: number | null;
  discountPercent: number | null; discountExpiresAt: string | null;
  effectivePrice: number; // price minus discount, server-computed — display this, not price
  stockQuantity: number; trackInventory: boolean;
  reorderThreshold: number | null; reorderQuantity: number | null;  // added 2026-08-15, §9.15b
  images: string[]; tags: string[];
  status: "Draft" | "Active" | "OutOfStock" | "Archived";
  variants: ProductVariantResponse[];  // added 2026-08-15 (§9.5); BUYABLE since 2026-08-16 (§9.22)
  // --- added 2026-08-16 (§9.25, §9.28, §9.31) ---
  costPrice: number | null;   // populated on BackOffice reads, always null on public Shop reads
  unitMargin: number | null;  // effectivePrice − costPrice; null when no cost is recorded
  averageRating: number; reviewCount: number;
  brand: string; barcode: string;
  weightKg: number | null;
  metaTitle: string; metaDescription: string;
  publishedAt: string | null; unpublishedAt: string | null;
  isFeatured: boolean; sortWeight: number; taxClass: string;
  isAvailable: boolean;
};
// Every §9B field below is optional and defaults to empty/null, so the older request body works.
type CreateProductRequest = {
  categoryId: string; name: string; slug?: string | null; sku: string; description: string;
  price: number; compareAtPrice?: number | null; stockQuantity: number; trackInventory: boolean;
  images?: string[] | null; tags?: string[] | null;
  variants?: ProductVariantRequest[] | null;
  costPrice?: number | null;   // §9.31 — prompt for this; COGS, margin and the balance sheet need it
  brand?: string; barcode?: string;
  weightKg?: number | null; lengthCm?: number | null; widthCm?: number | null; heightCm?: number | null;
  metaTitle?: string; metaDescription?: string;
  publishedAt?: string | null; unpublishedAt?: string | null;  // scheduled go-live / retirement
  isFeatured?: boolean; sortWeight?: number;                   // drive the storefront's Relevance sort
  taxClass?: string;                                           // resolved against Business.tax.classRates
};
type UpdateProductRequest = {
  categoryId: string; name: string; description: string; price: number;
  compareAtPrice?: number | null; discountPercent?: number | null; // 0–100
  discountExpiresAt?: string | null;
  trackInventory: boolean;
  reorderThreshold?: number | null; reorderQuantity?: number | null;
  images: string[]; tags: string[];
  variants?: ProductVariantRequest[] | null;
};
```

**`UpdateProductRequest` no longer has `stockQuantity` (changed 2026-08-15, §9.15c)** — a
general product edit can no longer silently reset stock as a side effect. Stock only changes
through checkout, order cancellation, or the new manual adjustment endpoint (§7.9) — build a
dedicated "Adjust Stock" action on the product detail screen rather than a stock field in the
main edit form.

**Product variants are catalog-only (§9.5).** `Product.Variants` is fully manageable here
(create/edit/reorder — no dedicated variant endpoints, just include the array in
`Create`/`UpdateProductRequest`), but Cart/Order still checkout against the bare product, not a
specific variant — don't build a "select variant, add to cart" flow expecting the Shop app to
support it yet, since it can't.

**Image upload (§9.5):** `POST .../products/{id}/images` is `multipart/form-data` with one
field, `file` — JPEG/PNG/WEBP/GIF only, 5MB max, and the server generates its own filename (an
uploaded file's original name is never trusted or preserved). Returns the full updated
`ProductResponse` with the new URL appended to `images` — no separate "confirm" step. Uploaded
files are served back from `{VITE_API_BASE_URL}/uploads/{businessId}/{file}`, publicly, no auth.
This is local disk storage on the backend today, not S3/Cloudinary — fine for now, but don't
assume URLs survive a backend redeploy on every hosting platform.

**New products start as `Draft`** and are invisible on the public Shop until PATCHed to
`Active` — the "Create Product" flow should make this obvious (e.g. a prominent "Publish"
button after creation, not a silent no-op).

**`POST` 409s once the Business hits its plan's product limit** (main blueprint §9.9) — e.g.
`"Your 'Trial' plan allows up to 20 product(s) per Business. Upgrade your plan to add more."`
There's no BackOffice-callable endpoint to check usage proactively — `GET /api/tenants/me/usage`
exists (see the note in §7.2) but is `TenantOwner`-only, so `BusinessAdmin`/`BusinessStaff`
can't call it. Handle this 409 reactively (surface the message on the failed create) rather than
trying to pre-check; flag a Business-scoped usage endpoint as a gap if proactive warnings turn
out to matter here.

### 7.5 Coupons — `/api/businesses/{businessId}/coupons`

| Method | Path | Roles | Body | Returns |
|---|---|---|---|---|
| GET | `` | any BackOffice role | — | `CouponResponse[]` |
| POST | `` | Admin, TenantOwner, Platform (**not** Staff, added 2026-08-15, §9.3) | `CreateCouponRequest` | `CouponResponse` |
| PUT | `/{couponId}` | Admin, TenantOwner, Platform (**not** Staff) | `UpdateCouponRequest` | `CouponResponse` |
| DELETE | `/{couponId}` | Admin, TenantOwner, Platform (**not** Staff) | — | 204 |

**Staff is read-only on coupons** — coupons directly control discounts/revenue, treated the
same as the destructive product/category actions. Hide or disable the create/edit/delete
actions in the coupon UI for `BusinessStaff` sessions rather than letting them hit a 403.

```ts
type CouponResponse = {
  id: string; businessId: string; code: string;
  discountType: "Percentage" | "FixedAmount"; discountValue: number;
  minOrderAmount: number | null; maxUses: number | null; usedCount: number;
  startsAt: string; expiresAt: string; isActive: boolean;
};
type CreateCouponRequest = {
  code: string; discountType: "Percentage" | "FixedAmount"; discountValue: number;
  minOrderAmount?: number | null; maxUses?: number | null;
  startsAt: string; expiresAt: string; // ISO 8601
};
type UpdateCouponRequest = { isActive: boolean; expiresAt: string; maxUses: number | null };
```

Coupon codes are stored uppercased server-side — display/normalize accordingly in the UI so
what's shown matches what customers actually have to type on the Shop.

### 7.6 Staff & customers (Admin/TenantOwner/Platform only — not Staff, not DeliveryAgent)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/businesses/{businessId}/staff` | — | `UserSummaryResponse[]` (Admin+Staff+DeliveryAgent accounts) |
| POST | `/api/businesses/{businessId}/staff` | `CreateStaffRequest` | `UserSummaryResponse` |
| PATCH | `/api/businesses/{businessId}/staff/{userId}/status` | bare string, e.g. `"Blocked"` | `UserSummaryResponse` |
| GET | `/api/businesses/{businessId}/customers` | — | `UserSummaryResponse[]` (read-only) |

```ts
type CreateStaffRequest = {
  fullName: string; email: string; password: string; phone: string;
  role: "BusinessAdmin" | "BusinessStaff" | "DeliveryAgent"; // only these 3 creatable here
};
```

Creating a `DeliveryAgent` here also creates their `DeliveryAgentProfile` automatically
server-side (status `Offline`, level 1, zero balance) — nothing extra to call.

**`role: "DeliveryAgent"` 409s if `business.deliveryModuleEnabled` is `false`** (§7.2) — grey
out or hide that role option in the staff-creation form when the flag is off, rather than
letting the request round-trip to fail.

**`POST` also 409s once the Business hits its plan's staff limit** (added 2026-08-15, main
blueprint §9.9), counting `BusinessAdmin` + `BusinessStaff` + `DeliveryAgent` together against
one cap — e.g. `"Your 'Trial' plan allows up to 3 staff member(s) per Business. Upgrade your
plan to add more."` Same caveat as §7.4's product limit: no `BusinessAdmin`/`BusinessStaff`-
callable endpoint to check this proactively, so handle the 409 reactively.

### 7.7 Delivery agents

| Method | Path | Roles | Body | Returns |
|---|---|---|---|---|
| GET | `/api/businesses/{businessId}/delivery-agents` | Admin, Staff, TenantOwner, Platform | — | `DeliveryAgentResponse[]` |
| GET | `/api/businesses/{businessId}/delivery-agents/me` | any BackOffice role | — | `DeliveryAgentResponse` (caller's own) |
| PATCH | `/api/businesses/{businessId}/delivery-agents/me/status` | any BackOffice role | `{ status }` | `DeliveryAgentResponse` |
| PATCH | `/api/businesses/{businessId}/delivery-agents/{userId}/status` | Admin, Staff, TenantOwner, Platform | `{ status }` | `DeliveryAgentResponse` |

```ts
type DeliveryAgentResponse = {
  id: string; businessId: string; userId: string;
  status: "Free" | "Busy" | "Offline" | "Blocked";
  completedDeliveries: number; deliveryCharge: number; levelCode: number; balance: number;
};
```

Note: unlike the bare-string `.../status` endpoints elsewhere, these two take a **JSON
object** `{ "status": "Free" }`, not a bare string — the request DTO here is
`UpdateDeliveryAgentStatusRequest`, an object with one field. Double-check this against the
bare-string pattern used by Products/Staff/Businesses/Orders (§6.3 in the SuperOffice doc, §7.4
here) before wiring it up — it's the one inconsistent endpoint shape in the API.

**`balance` is now written to (changed 2026-08-15, §9.7)** — when an assigned agent's order is
marked `Delivered`, they're credited their flat `deliveryCharge` and `completedDeliveries`
increments by 1. It's a running total with no per-payout history exposed here (the backend
tracks that internally as `LedgerEntry` rows for accounting purposes — §7.10 — but there's no
BackOffice-facing "payout history" endpoint). Safe to build simple "current balance" /
"deliveries completed" UI around it now.

### 7.8 Orders — `/api/businesses/{businessId}/orders`

| Method | Path | Roles | Body | Returns |
|---|---|---|---|---|
| GET | `?...OrderQuery` | Admin, Staff, TenantOwner, Platform (**not** DeliveryAgent) | — | **`PagedResult<OrderResponse>`** |
| GET | `/assigned-to-me?page=&pageSize=` | any BackOffice role | — | `PagedResult<OrderResponse>` (DeliveryAgent's own queue; empty for other roles) |
| GET | `/{orderId}` | Admin, Staff, TenantOwner, Platform, DeliveryAgent | — | `OrderResponse` |
| PATCH | `/{orderId}/status` | Admin, Staff, TenantOwner, Platform, DeliveryAgent | `{ status, note }` | `OrderResponse` |
| PATCH | `/{orderId}/payment-status` | Admin, Staff, TenantOwner, Platform | `{ status, note? }` | `OrderResponse` (added 2026-08-15, §9.6) |
| PATCH | `/{orderId}/assign-delivery` | Admin, Staff, TenantOwner, Platform (**not** DeliveryAgent) | `{ deliveryAgentUserId }` | `OrderResponse` |
| PATCH | `/{orderId}/shipment` | Admin, Staff, TenantOwner, Platform | `UpdateShipmentRequest` | `OrderResponse` (added 2026-08-16, §9.20) |
| PATCH | `/{orderId}/internal-note` | Admin, Staff, TenantOwner, Platform | `UpdateOrderNoteRequest` | `OrderResponse` (added 2026-08-16) |
| GET | `/{orderId}/invoice` | Admin, Staff, TenantOwner, Platform | — | `InvoiceResponse` (added 2026-08-16, §9.33) |

**Recording a tracking number ships the order.** `PATCH .../shipment` also moves a `Processing`
or `Confirmed` order to `OutForDelivery` and emails the customer — so it is the external-courier
counterpart to `assign-delivery`, not just a metadata edit. Don't make staff perform a separate
status change afterwards; they'll forget.

**`GET .../invoice` assigns the invoice number on first call** and returns the same one forever
after. The number is gapless and sequential per Business, so calling it is not free — trigger it
from an explicit "Issue invoice" action rather than on page load, or every browsed order gets a
number. The response carries seller legal identity, tax registration, both addresses, itemised
lines, discounts, tax and amount paid/due; rendering the PDF is this app's job.

**`assign-delivery` 409s the same way if `business.deliveryModuleEnabled` is `false`** — hide
the "assign delivery agent" action on the order detail screen when the flag is off (§7.2).

```ts
type OrderStatusEventResponse = { status: OrderResponse["status"]; timestamp: string; note: string };
type PaymentStatusEventResponse = { status: OrderResponse["paymentStatus"]; timestamp: string; note: string };
type Address = {
  label: string; line1: string; line2: string; city: string; state: string;
  postalCode: string; country: string; phone: string; isDefault: boolean;
};
type OrderResponse = {
  id: string; businessId: string; orderNumber: string;
  customerUserId: string;            // "" for a guest order (§9.27)
  isGuestOrder: boolean;
  contactEmail: string; contactPhone: string;
  items: {
    productId: string; variantId: string | null; variantSummary: string | null;
    productName: string; unitPrice: number; quantity: number;
    refundedQuantity: number;        // §9.21
    lineTotal: number;
  }[];
  subtotal: number; couponCode: string | null; discountAmount: number;
  discounts: { source: string; label: string; amount: number }[];
  deliveryFee: number;
  taxAmount: number; taxRatePercent: number; pricesIncludeTax: boolean;   // §9.19, snapshotted
  total: number;
  giftCardTotal: number;
  giftCardsUsed: { codeSuffix: string; amountApplied: number }[];
  storeCreditApplied: number; amountDue: number; refundedAmount: number;
  currency: string;                  // §9.38 — snapshotted at checkout; display with THIS
  status: "PendingPayment" | "Processing" | "Confirmed" | "OutForDelivery" | "Delivered" | "Cancelled" | "Refunded";
  paymentStatus: "Pending" | "Paid" | "Failed" | "Refunded";
  fulfillmentMethod: "Delivery" | "Pickup" | "ExternalCourier" | "Digital";
  shippingAddress: Address | null;
  billingAddress: Address | null;
  deliveryAgentUserId: string | null;
  shippingMethodName: string | null;
  carrierName: string | null; trackingNumber: string | null; trackingUrl: string | null;  // §9.20
  invoiceNumber: string | null; customerNote: string;
  statusHistory: OrderStatusEventResponse[];         // added 2026-08-15, §9.6/§9.7
  paymentStatusHistory: PaymentStatusEventResponse[]; // added 2026-08-15, §9.6
  placedAt: string;
};

// --- added 2026-08-16 ---
type OrderQuery = {
  status?: string; paymentStatus?: string; search?: string;   // search matches orderNumber / contactEmail
  from?: string; to?: string; page?: number; pageSize?: number;
};
type UpdateShipmentRequest = {
  carrierName: string | null; trackingNumber: string | null;
  trackingUrl: string | null; shippingMethodName: string | null;
};
type UpdateOrderNoteRequest = { internalNote: string };  // staff-only, never shown to the customer
type UpdateOrderStatusRequest = { status: OrderResponse["status"]; note: string };
type UpdatePaymentStatusRequest = { status: OrderResponse["paymentStatus"]; note?: string | null };
type AssignDeliveryAgentRequest = { deliveryAgentUserId: string };
```

**A real order status state machine is now enforced server-side (changed 2026-08-15, §9.7)** —
the old "build the UI to only offer sensible transitions, but the API won't stop a client that
skips steps" caveat no longer applies; the API itself now rejects illegal jumps with a 409
naming the from/to states (e.g. `"Cannot move an order from 'Processing' to 'Delivered'."`).
Legal transitions: `PendingPayment`→Processing/Cancelled, `Processing`→Confirmed/
OutForDelivery/Cancelled, `Confirmed`→OutForDelivery/Cancelled, `OutForDelivery`→Delivered/
Cancelled, `Delivered`→Refunded only. `Cancelled`/`Refunded` are terminal. Still build the UI to
only *offer* the legal next steps (better UX than a round-trip 409), but it's now a genuine
backend guarantee, not just a client-side convention — a status dropdown can safely be
constrained to `statusHistory[statusHistory.length - 1].status`'s legal transitions.

Setting status to `Cancelled` **restocks the order's items automatically** server-side
(`TrackInventory` products only) — no separate restock call needed. Setting status to
`Delivered` **credits the assigned delivery agent's balance** automatically (§7.7) and marks
revenue as recognized for accounting purposes (§7.10) — both side effects, no separate call
needed for either.

**`statusHistory`/`paymentStatusHistory` are now exposed** — a status-history timeline on the
order detail screen (which the previous version of this doc flagged as needing a backend
addition) can now be built directly from these arrays, no extra request.

**Payment is still manual, not gateway-integrated (§9.6).** `paymentStatus` starts at `Pending`;
`PATCH .../payment-status` lets BackOffice staff record a payment manually (e.g. "cash
collected on delivery") — build a "Mark as Paid" action on the order detail screen using this.
There's still no real gateway behind it — no webhook will ever call this automatically; a human
always has to.

### 7.9 Inventory (added 2026-08-15, main blueprint §9.15)

| Method | Path | Roles | Body | Returns |
|---|---|---|---|---|
| GET | `/api/businesses/{businessId}/products/{productId}/stock-movements` | any BackOffice role | — | `StockMovementResponse[]` |
| POST | `/api/businesses/{businessId}/products/{productId}/stock-adjustments` | any BackOffice role | `AdjustStockRequest` | `ProductResponse` |
| GET | `/api/businesses/{businessId}/inventory/low-stock` | any BackOffice role | — | `LowStockProductResponse[]` |
| GET | `/api/businesses/{businessId}/inventory/valuation` | any BackOffice role | — | `InventoryValuationResponse` |

```ts
type StockMovementResponse = {
  id: string; productId: string;
  type: "Sale" | "Restock" | "Return" | "Adjustment" | "DamageWriteOff";
  quantityDelta: number;  // signed — negative removes stock
  reason: string; referenceOrderId: string | null; createdByUserId: string | null; createdAt: string;
};
type AdjustStockRequest = {
  quantityDelta: number; reason: string;
  type: "Restock" | "Adjustment" | "DamageWriteOff";  // Sale/Return 403 here — system-generated only
};
type LowStockProductResponse = {
  productId: string; productName: string; sku: string;
  stockQuantity: number; reorderThreshold: number; reorderQuantity: number | null;
};
// CHANGED 2026-08-16 (§9.31) — `totalValue` is gone; it valued stock at RETAIL price and fed
// that to the balance sheet as an asset, overstating assets by the whole unrealised margin.
type CategoryValuationEntry = { categoryId: string; valueAtCost: number; valueAtRetail: number };
type InventoryValuationResponse = {
  totalValueAtCost: number;      // the accounting figure — what the balance sheet uses
  totalValueAtRetail: number;    // what it would sell for; merchandising info, NOT an asset value
  potentialMargin: number;
  unvaluedProductCount: number;  // products in stock with no costPrice recorded — surface this,
                                 // it's the honest measure of how much the cost figure understates
  byCategory: CategoryValuationEntry[];
};
```

**Every `StockQuantity` change is now logged.** Checkout, order cancellation, and this manual
adjustment endpoint are the *only* three ways stock moves — a product edit (§7.4) can no longer
touch it. Build the product detail screen's "Adjust Stock" action around `POST
.../stock-adjustments`, and a "Stock History" tab around `GET .../stock-movements`.

**Low-stock alerts are pull, not push.** `GET .../inventory/low-stock` is a snapshot query, not
a subscription/webhook — poll it or check it on a dashboard load. Only products with a
`reorderThreshold` set (via §7.4's `UpdateProductRequest`) appear here at all, even if their
stock is genuinely low — the UI should nudge staff to set a threshold on products that don't
have one if low-stock visibility matters to them.

### 7.10 Accounting (added 2026-08-15, main blueprint §9.16 — Admin-tier only, not Staff)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/businesses/{businessId}/expenses` | — | `ExpenseResponse[]` |
| POST | `/api/businesses/{businessId}/expenses` | `CreateExpenseRequest` | `ExpenseResponse` |
| PUT | `/api/businesses/{businessId}/expenses/{expenseId}` | `UpdateExpenseRequest` | `ExpenseResponse` |
| DELETE | `/api/businesses/{businessId}/expenses/{expenseId}` | — | 204 |
| GET | `/api/businesses/{businessId}/accounting/profit-and-loss?from&to` | — | `ProfitAndLossResponse` |
| GET | `/api/businesses/{businessId}/accounting/balance-sheet` | — | `BalanceSheetResponse` |

```ts
type ExpenseResponse = {
  id: string; businessId: string; category: string; amount: number;
  note: string; incurredAt: string; createdByUserId: string;
};
type CreateExpenseRequest = { category: string; amount: number; note: string; incurredAt: string };
type UpdateExpenseRequest = { category: string; amount: number; note: string; incurredAt: string };
// CHANGED 2026-08-16 (§9.31) — two profit lines now, and NetProfit finally subtracts COGS.
type ProfitAndLossResponse = {
  from: string; to: string;
  revenue: number;              // NET OF TAX — tax collected is a liability, reported separately
  refunds: number;
  costOfGoodsSold: number;
  grossProfit: number;          // revenue − refunds − COGS. The number most retailers manage on.
  grossMarginPercent: number;
  expenses: number;
  deliveryPayouts: number;
  netProfit: number;            // grossProfit − expenses − deliveryPayouts
  taxCollected: number;
  uncostedOrderCount: number;   // delivered orders in-window with no recorded cost — see note
};
type BalanceSheetResponse = {
  cashPosition: number;
  inventoryValueAtCost: number;   // replaces the old `inventoryValue`, which used retail price
  inventoryValueAtRetail: number;
  totalAssets: number;
  taxPayable: number;             // collected, owed onward — not the business's money
  giftCardLiability: number;      // outstanding gift-card float
  totalLiabilities: number;
  netPosition: number;            // totalAssets − totalLiabilities
};
```

`from`/`to` on the P&L endpoint are required query params, ISO 8601 (e.g.
`?from=2026-01-01T00:00:00Z&to=2026-12-31T23:59:59Z`) — there's no default window, the caller
always picks one.

**What changed on 2026-08-16, and why it matters to what you render (§9.31).** The old
`netProfit` never subtracted what the goods cost, so it was inflated by the entire cost of
everything sold. It does now, and there is a `grossProfit`/`grossMarginPercent` pair alongside
it — margin is the number most retailers actually manage on day to day, so give it prominence
at least equal to net profit.

**Surface `uncostedOrderCount` when it's non-zero.** It counts delivered orders in the window
that contributed no cost, meaning gross profit is optimistic by an unknown amount. A quiet
inline warning — "N orders have no recorded cost price; margin is overstated" — linking to the
products missing a `costPrice` is far better than a confident wrong number. Same for
`unvaluedProductCount` on the valuation screen.

**Prompt for `costPrice` in the product form.** Without it, COGS, gross margin and the balance
sheet's asset figure are all incomplete. It is the single highest-value field in §7.4.

**Still single-entry, cash-basis bookkeeping, not a GAAP-compliant system** — `revenue` is
recognized only when an order reaches `Delivered` (the same rule §7.16's dashboard and the
SuperOffice analytics endpoint use, so the three never quietly disagree). The balance sheet now
*has* a liabilities side (tax payable, gift-card float) but it is still partial: no supplier
payables, no accruals. Frame these screens as an operational dashboard for the business owner,
not as something to hand to an accountant as a system of record.

### 7.11 Returns / RMAs (added 2026-08-16, main blueprint §9.21)

| Method | Path | Body | Returns | Who |
|---|---|---|---|---|
| GET | `.../returns?status=&page=&pageSize=` | — | `PagedResult<ReturnResponse>` | Staff+ |
| GET | `.../returns/{returnId}` | — | `ReturnResponse` | Staff+ |
| POST | `.../returns/{returnId}/decision` | `DecideReturnRequest` | `ReturnResponse` | Staff+ |
| POST | `.../returns/{returnId}/received` | — | `ReturnResponse` | Staff+ |
| POST | `.../returns/{returnId}/refund` | — | `ReturnResponse` | **Admin-tier** |

```ts
type DecideReturnRequest = {
  approve: boolean;
  approvedRefundAmount: number | null;  // null = the full requested amount; can be LESS, never more
  note: string;
};
// ReturnResponse is the same shape the Shop blueprint documents.
```

**The lifecycle is `Requested → Approved → Received → Refunded`, and each step does something
different.** Build the UI as three distinct actions, not one "process return" button:

- **Decision** — approve or reject. Approving with a lower `approvedRefundAmount` is how a
  restocking fee is applied. More than requested is a `409`.
- **Received** — the goods are physically back. **This is what restocks**, not approval and not
  the refund. Damaged returns don't restock at all (a `DamageWriteOff` note is written instead).
- **Refund** — Admin-tier, writes the ledger entry and settles the money. Refusing until
  `Received` is deliberate; don't try to shortcut it.

A **partial** return leaves the order `Delivered` and only moves `refundedAmount` /
`refundedQuantity`. Show a "partially refunded" badge off those fields rather than reading the
order status.

### 7.12 Reviews (added 2026-08-16, main blueprint §9.25)

| Method | Path | Body | Returns | Who |
|---|---|---|---|---|
| GET | `.../reviews?status=&page=&pageSize=` | — | `PagedResult<ReviewResponse>` | Staff+ |
| PATCH | `.../reviews/{reviewId}/status` | `{ status: "Pending" \| "Published" \| "Rejected" }` | `ReviewResponse` | Staff+ |
| POST | `.../reviews/{reviewId}/reply` | `{ reply: string }` | `ReviewResponse` | Staff+ |
| DELETE | `.../reviews/{reviewId}` | — | 204 | **Admin-tier** |

Reviews land `Pending` unless the Business sets `autoPublishReviews` (§7.2) — a moderation queue
filtered to `status=Pending` is the primary screen, with the count worth badging in the nav.
`isVerifiedPurchase` is server-established against real delivered orders; show it in the queue,
it's the strongest signal a review is genuine. Moderating in either direction recomputes the
product's `averageRating`, so unpublishing genuinely lowers it.

### 7.13 Merchandising — Admin-tier only (added 2026-08-16, §9.20, §9.23, §9.24, §9.30)

All under `/api/businesses/{businessId}`. Full CRUD on each unless noted.

| Area | Paths |
|---|---|
| Promotions (§9.23) | `GET/POST .../promotions`, `PUT/DELETE .../promotions/{id}` |
| Customer groups (§9.23) | `GET/POST .../customer-groups`, `PUT/DELETE .../customer-groups/{id}`, `POST/DELETE .../customer-groups/{id}/members` |
| Gift cards (§9.24) | `GET/POST .../gift-cards`, `DELETE .../gift-cards/{id}` (deactivate) |
| Store credit (§9.24) | `GET/POST .../customers/{customerUserId}/store-credit` |
| Shipping zones (§9.20) | `GET/POST .../shipping-zones`, `PUT/DELETE .../shipping-zones/{id}` |
| Storefront content (§9.30) | `GET/POST .../content?type=`, `PUT/DELETE .../content/{id}` |

```ts
type CreatePromotionRequest = {
  name: string;
  code: string | null;              // null = AUTOMATIC, applies with no code typed
  effect: "PercentageOff" | "FixedAmountOff" | "FreeShipping" | "BuyXGetY";
  scope: "Order" | "Products" | "Categories";
  value: number;                    // percent, or currency amount; ignored for FreeShipping/BuyXGetY
  productIds: string[]; categoryIds: string[];
  buyQuantity: number; getQuantity: number;   // BuyXGetY only
  minOrderAmount: number | null;
  customerGroupIds: string[];       // empty = everyone
  firstOrderOnly: boolean;
  maxUses: number | null;           // global cap
  maxUsesPerCustomer: number | null;// the per-customer cap Coupon never had
  priority: number;                 // lower runs first
  stackable: boolean;               // false = this one wins and suppresses everything lower-priority
  startsAt: string; endsAt: string | null; isActive: boolean;
};
type CustomerGroupRequest = { name: string; description: string; discountPercent: number | null; isActive: boolean };
type IssueGiftCardRequest = { amount: number; issuedToEmail: string | null; expiresAt: string | null };
type GrantStoreCreditRequest = { amount: number; note: string };   // negative amounts deduct
type CreateShippingZoneRequest = {
  name: string;
  countries: string[];              // EMPTY = the catch-all fallback zone
  regions: string[];                // narrows within those countries
  rates: {
    name: string; price: number;
    minOrderSubtotal: number | null; maxOrderSubtotal: number | null;   // free-shipping-over-X lives here
    minWeightKg: number | null; maxWeightKg: number | null;
    estimatedDaysMin: number | null; estimatedDaysMax: number | null;
    isActive: boolean;
  }[];
  priority: number;                 // lower wins among equally specific zones
  isActive: boolean;
};
type ContentBlockRequest = {
  type: "Banner" | "Page" | "MenuItem" | "Article";
  slug: string; title: string; subtitle: string; body: string;  // Markdown
  imageUrl: string; linkUrl: string; linkLabel: string;
  parentId: string | null;          // MenuItem only, one level of nesting
  sortOrder: number; isPublished: boolean;
  startsAt: string | null; endsAt: string | null;   // scheduled visibility
  metaTitle: string; metaDescription: string;
};
```

Notes worth building around:

- **`Coupon` (§7.5) still exists and still works.** Promotions sit alongside it, not on top —
  every existing code keeps behaving as it did. In the UI, present coupons as "simple codes" and
  promotions as "campaigns"; don't merge the two screens.
- **A gift card's plaintext code is returned exactly once, from the create call.** Show it in a
  copy-to-clipboard dialog and say it can't be retrieved later. Every later read shows only
  `codeSuffix`.
- **Rate ids are server-generated.** Editing a zone replaces its whole `rates` array and issues
  new ids — don't try to preserve them client-side.
- **Terms and Privacy pages are merchant-authored `Page` blocks.** They are legally required in
  most markets, and there was nowhere to put them before this. Consider seeding empty drafts.

### 7.14 CSV import / export (added 2026-08-16, §9.28 — Admin-tier only)

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `.../products/import` | `multipart/form-data`, field `file` (max 5 MB) | `ProductImportResult` |
| GET | `.../products/export` | — | `text/csv` download |

```ts
type ProductImportResult = { created: number; updated: number; skipped: number; errors: string[] };
```

Header row, in this order: `sku,name,category,description,price,costPrice,stockQuantity,brand,
barcode,weightKg,tags` (tags pipe-separated). **Upsert keyed on SKU** — re-importing updates
rather than duplicating, and `status` is deliberately left alone so a round-trip can't
republish something the merchant took down. A row naming a category that doesn't exist is
skipped and reported, not auto-created. Export produces exactly the import format, so
"export → edit in Excel → import" is the intended loop. Render `errors` as a per-row list; it
is the whole value of the response.

### 7.15 Audit log (added 2026-08-16, §9.35 — Admin-tier only)

`GET .../audit-log?userId=&resourceId=&from=&to=&page=&pageSize=` → `PagedResult<AuditLogResponse>`

```ts
type AuditLogResponse = {
  id: string; userId: string; userEmail: string; role: string;
  method: string; path: string; routeTemplate: string; statusCode: number;
  ipAddress: string; resourceId: string | null; durationMs: number; createdAt: string;
};
```

Every mutating request is recorded — who, what route, what outcome. **Entries are HTTP-shaped,
not domain-shaped:** you get "this user DELETEd this product and got a 204", not a before/after
field diff. Render it as an activity feed grouped by user and day, and set expectations
accordingly rather than promising a change history. Admin-tier only, deliberately: it names the
staff who performed each action.

### 7.16 Dashboard (added 2026-08-16, §9.32 — Admin-tier only)

`GET .../analytics/dashboard?from=&to=` → `BusinessDashboardResponse`. Both params optional;
defaults to the last 30 days.

```ts
type BusinessDashboardResponse = {
  from: string; to: string;
  revenue: number; grossProfit: number;
  orderCount: number; deliveredCount: number; cancelledCount: number;
  averageOrderValue: number;
  uniqueCustomers: number; repeatCustomers: number; repeatCustomerRate: number; newCustomers: number;
  pendingReturns: number; lowStockCount: number;
  dailySales: { date: string; revenue: number; orderCount: number }[];
  topProducts: { productId: string; productName: string; quantitySold: number; revenue: number }[];
  statusBreakdown: { status: string; count: number }[];
  currency: string;
};
```

**This replaces the client-side assembly this document used to recommend.** There is one request
for the whole dashboard now. Revenue is recognised on `Delivered` only — the same rule the P&L
and SuperOffice analytics use, so the numbers on different screens agree. `orderCount` is
broader (every non-cancelled order), because pipeline activity is worth seeing before it becomes
revenue; don't present the two as if they measure the same thing.

---

## 8. Recommended pages

1. **Login** (with resolved Business branding pre-login, per §3), plus a "Forgot password?"
   link (§5) — worth building the screen even though real email delivery doesn't exist yet
   (§7.1's note), so it's ready the moment a provider is wired in.
2. **Dashboard** — **one request now**: `GET .../analytics/dashboard` (§7.16). Revenue, gross
   profit, AOV, repeat-customer rate, a daily sales chart, top products, a status funnel,
   pending returns and low stock all come back together. The client-side assembly this document
   used to recommend is obsolete. Admin-tier only (§9.3, financial data) — Staff sessions still
   can't call it, so give them the low-stock list and an order-count widget instead.
3. **Business Profile** (§7.2) — including the new `defaultDeliveryFee` field.
4. **Categories** (§7.3) — flat list or the new tree view; create/edit for everyone, delete
   Admin-tier only.
5. **Products** (§7.4) — paged list with server-side `search`, status filter/badges,
   create/edit (variants, reorder threshold, and **`costPrice` — prompt for it, everything in
   §7.10 depends on it**, plus weight/brand/barcode/SEO/publish-window), image upload, publish
   (Draft→Active) as a distinct visible action, delete Admin-tier only, an "Adjust Stock" action
   opening onto §7.9's endpoint instead of a stock field in the main edit form, and CSV
   import/export (§7.14) for bulk work.
6. **Coupons** (§7.5) — read-only for Staff, full CRUD for Admin-tier.
7. **Inventory** (§7.9) — low-stock list, valuation summary, stock-movement history per
   product.
8. **Accounting** (§7.10, Admin-tier only, hide entirely from Staff) — expense list/CRUD, a
   date-range P&L view, a balance-sheet snapshot.
9. **Staff** (§7.6) — list, invite/create, block/unblock.
10. **Customers** (§7.6) — read-only list.
11. **Delivery Agents** (§7.7) — list, status management, balance/completed-deliveries display.
12. **Orders** (§7.8) — paged list with **server-side** status/payment/date/search filtering
    (stop filtering in the browser), detail view with a real status-history and
    payment-status-history timeline, a status dropdown constrained to the current status's legal
    next steps, a "Mark as Paid" action, delivery assignment, plus (new 2026-08-16) an external
    courier tracking form, an internal-notes field, and an "Issue invoice" action off
    `GET .../orders/{id}/invoice` — the response carries everything a printable invoice needs;
    rendering it as a PDF is this app's job, the API supplies the data.

**New pages for the §9B areas** (all Admin-tier unless noted):

15. **Returns queue** (§7.11) — the three-step decision → received → refund flow, Staff-visible
    except the refund action. Badge the pending count in the nav.
16. **Review moderation** (§7.12) — queue filtered to `Pending`, with verified-purchase badges
    and a merchant-reply box. Staff-visible except delete.
17. **Promotions & customer groups** (§7.13) — campaigns are a genuinely different mental model
    from coupons; keep the screens separate and don't merge them into §7.5.
18. **Gift cards & store credit** (§7.13) — issue dialog that shows the plaintext code **once**,
    a card list by suffix, and a per-customer credit statement.
19. **Shipping zones** (§7.13) — zone list with a rate-table editor per zone.
20. **Storefront content** (§7.13) — banners with schedules, a page editor (Markdown) that seeds
    Terms and Privacy drafts, and a nav-menu builder.
21. **Audit log** (§7.15) — activity feed, filterable by user, resource and date.
22. **Settings → Tax & invoicing** (§7.2's `UpdateBusinessRequest`) — tax rate, inclusive/
    exclusive pricing, registration numbers, invoice prefix and seller legal identity, return
    window, review auto-publish, guest-checkout toggle. Every one of these changes what the
    storefront renders or what customers are charged, so it belongs behind Admin-tier.
13. **DeliveryAgent-only shell** (§4): My Deliveries (`GET .../orders/assigned-to-me`), My
    Status (`GET`/`PATCH .../delivery-agents/me`), status update on individual assigned orders.
14. **My Profile** (`GET`/`PUT /api/auth/me`).
