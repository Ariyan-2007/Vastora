# BackOffice Frontend Blueprint

**Self-contained.** This file assumes no other context — move it into the BackOffice
project's own repo and hand it to a fresh session; everything needed to build against the
Vastora API is here. If the Vastora API changes, this doc must be updated in the same session
as the change (see the main `VASTORA_BLUEPRINT.md`, §10, in the Vastora backend repo).

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
| Categories | ✅ | ✅ | — | ✅ |
| Products | ✅ | ✅ | — | ✅ |
| Coupons | ✅ | ✅ | — | ✅ |
| Staff (list/create/status) | ✅ | — | — | ✅ |
| Customers (read-only list) | ✅ | — | — | ✅ |
| Delivery agents (list/set any status) | ✅ | ✅ | — | ✅ |
| My delivery status (self) | — | — | ✅ | — |
| Orders (list all / detail / status / assign) | ✅ | ✅ | — | ✅ |
| My assigned deliveries (self) | — | — | ✅ | — |
| Order status update | ✅ | ✅ | ✅ (their own assigned orders, same endpoint) | ✅ |

**`BusinessAdmin` and `BusinessStaff` currently have identical permissions on the backend** —
the roles exist and are stored, but nothing differentiates them yet (tracked as a backend
follow-up). Build the nav as shown above (matching what the API actually allows today) rather
than inventing a Staff-restricted view the backend won't enforce — that would create a false
sense of security. One exception already enforced: only `BusinessAdmin` (not `BusinessStaff`)
can edit the Business profile itself.

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

**Known gap:** raw DTO shape validation (empty strings, malformed values) isn't fully enforced
server-side yet for every endpoint — validate defensively client-side. Business-rule
violations (duplicate codes, etc.) *are* enforced today via typed exceptions.

**Every `PATCH .../status` endpoint takes a bare JSON string as its body**, not an object —
e.g. `PATCH .../products/{id}/status` body is literally `"Active"` (quoted string), not
`{"status": "Active"}`. This is consistent across the whole API, not a mistake.

---

## 7. API reference

Base URL = `VITE_API_BASE_URL`. `{businessId}` = the ID resolved in §3 (**not** the slug).
All endpoints below require `Authorization: Bearer <token>` and the caller must be entitled to
that `businessId` (own-Business for `BusinessAdmin`/`BusinessStaff`/`DeliveryAgent`, any
Business under their Tenant for `TenantOwner`, any Business at all for `PlatformSuperAdmin`).

### 7.1 Auth (see also §5)

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `/api/auth/login` | `{ email, password }` | `AuthResponse` |
| POST | `/api/auth/refresh` | `{ refreshToken }` | `AuthResponse` |
| POST | `/api/auth/logout` | `{ refreshToken }` | 204 |
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

```ts
type BusinessResponse = {
  id: string; tenantId: string; name: string; slug: string; customDomain: string | null;
  description: string; logoUrl: string; bannerUrl: string; themeColor: string;
  currency: string; contactEmail: string; contactPhone: string;
  status: "Draft" | "Active" | "Suspended"; createdAt: string;
};
type UpdateBusinessRequest = {
  name: string; description: string; logoUrl: string; bannerUrl: string;
  themeColor: string; contactEmail: string; contactPhone: string; currency: string;
};
```

### 7.3 Categories — `/api/businesses/{businessId}/categories`

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `` | — | `CategoryResponse[]` |
| GET | `/{categoryId}` | — | `CategoryResponse` |
| POST | `` | `CreateCategoryRequest` | `CategoryResponse` |
| PUT | `/{categoryId}` | `UpdateCategoryRequest` | `CategoryResponse` |
| DELETE | `/{categoryId}` | — | 204 |

```ts
type CategoryResponse = {
  id: string; businessId: string; name: string; slug: string;
  parentCategoryId: string | null; description: string; imageUrl: string;
  sortOrder: number; isActive: boolean;
};
type CreateCategoryRequest = {
  name: string; slug?: string | null; parentCategoryId?: string | null;
  description: string; imageUrl: string; sortOrder: number;
};
type UpdateCategoryRequest = {
  name: string; description: string; imageUrl: string; sortOrder: number; isActive: boolean;
};
```

`parentCategoryId` exists for subcategories but the API returns a **flat list** — no
tree-building happens server-side. If a nested category UI is wanted, build the tree
client-side from the flat list, or treat categories as flat for now (simplest, matches what
most small shops need).

### 7.4 Products — `/api/businesses/{businessId}/products`

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `` | — | `ProductResponse[]` (every status, incl. Draft/Archived — this is the BackOffice view) |
| GET | `/{productId}` | — | `ProductResponse` |
| POST | `` | `CreateProductRequest` | `ProductResponse` (created as `Draft`) |
| PUT | `/{productId}` | `UpdateProductRequest` | `ProductResponse` |
| PATCH | `/{productId}/status` | bare string, e.g. `"Active"` | `ProductResponse` |
| DELETE | `/{productId}` | — | 204 |

```ts
type ProductResponse = {
  id: string; businessId: string; categoryId: string; name: string; slug: string; sku: string;
  description: string; price: number; compareAtPrice: number | null;
  discountPercent: number | null; discountExpiresAt: string | null;
  effectivePrice: number; // price minus discount, server-computed — display this, not price
  stockQuantity: number; trackInventory: boolean;
  images: string[]; tags: string[];
  status: "Draft" | "Active" | "OutOfStock" | "Archived";
};
type CreateProductRequest = {
  categoryId: string; name: string; slug?: string | null; sku: string; description: string;
  price: number; compareAtPrice?: number | null; stockQuantity: number; trackInventory: boolean;
  images?: string[] | null; tags?: string[] | null;
};
type UpdateProductRequest = {
  categoryId: string; name: string; description: string; price: number;
  compareAtPrice?: number | null; discountPercent?: number | null; // 0–100
  discountExpiresAt?: string | null; stockQuantity: number; trackInventory: boolean;
  images: string[]; tags: string[];
};
```

**New products start as `Draft`** and are invisible on the public Shop until PATCHed to
`Active` — the "Create Product" flow should make this obvious (e.g. a prominent "Publish"
button after creation, not a silent no-op). There is no image upload endpoint — `images` is a
plain string array of URLs the caller supplies; host images elsewhere (S3/Cloudinary/whatever)
and paste URLs, or plan to add an upload endpoint to the backend first if that's a blocker.

### 7.5 Coupons — `/api/businesses/{businessId}/coupons`

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `` | — | `CouponResponse[]` |
| POST | `` | `CreateCouponRequest` | `CouponResponse` |
| PUT | `/{couponId}` | `UpdateCouponRequest` | `CouponResponse` |
| DELETE | `/{couponId}` | — | 204 |

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

`balance` is currently never written to by any endpoint (no payout/earnings logic exists yet)
— it will always read `0`. Don't build payout UI around it yet.

### 7.8 Orders — `/api/businesses/{businessId}/orders`

| Method | Path | Roles | Body | Returns |
|---|---|---|---|---|
| GET | `` | Admin, Staff, TenantOwner, Platform (**not** DeliveryAgent) | — | `OrderResponse[]` |
| GET | `/assigned-to-me` | any BackOffice role | — | `OrderResponse[]` (DeliveryAgent's own queue; empty for other roles) |
| GET | `/{orderId}` | Admin, Staff, TenantOwner, Platform, DeliveryAgent | — | `OrderResponse` |
| PATCH | `/{orderId}/status` | Admin, Staff, TenantOwner, Platform, DeliveryAgent | `{ status, note }` | `OrderResponse` |
| PATCH | `/{orderId}/assign-delivery` | Admin, Staff, TenantOwner, Platform (**not** DeliveryAgent) | `{ deliveryAgentUserId }` | `OrderResponse` |

```ts
type OrderResponse = {
  id: string; businessId: string; orderNumber: string; customerUserId: string;
  items: { productId: string; productName: string; unitPrice: number; quantity: number; lineTotal: number }[];
  subtotal: number; couponCode: string | null; discountAmount: number; deliveryFee: number; total: number;
  status: "PendingPayment" | "Processing" | "Confirmed" | "OutForDelivery" | "Delivered" | "Cancelled" | "Refunded";
  paymentStatus: "Pending" | "Paid" | "Failed" | "Refunded";
  shippingAddress: {
    label: string; line1: string; line2: string; city: string; state: string;
    postalCode: string; country: string; phone: string; isDefault: boolean;
  } | null;
  deliveryAgentUserId: string | null;
  placedAt: string;
};
type UpdateOrderStatusRequest = { status: OrderResponse["status"]; note: string };
type AssignDeliveryAgentRequest = { deliveryAgentUserId: string };
```

**No order status state machine is enforced server-side yet** — any allowed caller can set any
`OrderStatus` on any order (e.g. `Delivered` straight from `PendingPayment` is currently
possible via the API). Build the UI to only *offer* sensible forward transitions
(PendingPayment/Processing → Confirmed → OutForDelivery → Delivered, with Cancelled available
from the earlier states) even though the API won't stop a client that skips steps — that
constraint is a documented backend gap, not something to rely on.

Setting status to `Cancelled` **restocks the order's items automatically** server-side
(`TrackInventory` products only) — no separate restock call needed.

**No payment gateway integration exists.** `paymentStatus` starts at `Pending` and nothing in
the current API ever changes it — don't build a "mark as paid" flow expecting it to trigger
anything beyond updating that one field's display value (there's no PATCH for payment status
specifically at all right now, only full order status).

---

## 8. Recommended pages

1. **Login** (with resolved Business branding pre-login, per §3).
2. **Dashboard** — no aggregate stats endpoint exists (backend roadmap gap); a reasonable MVP
   dashboard computes simple counts client-side from list responses already being fetched
   elsewhere (e.g. "12 orders", "3 low-stock products" from the Products list where
   `stockQuantity` is low) rather than calling anything new.
3. **Business Profile** (§7.2).
4. **Categories** (§7.3) — flat list, create/edit/delete.
5. **Products** (§7.4) — list with status filter/badges, create/edit, publish (Draft→Active)
   as a distinct visible action, stock editing.
6. **Coupons** (§7.5).
7. **Staff** (§7.6) — list, invite/create, block/unblock.
8. **Customers** (§7.6) — read-only list.
9. **Delivery Agents** (§7.7) — list, status management.
10. **Orders** (§7.8) — list with status filter, detail view with status-history timeline
    (`OrderResponse` doesn't currently expose the history array to this DTO — only current
    `status`; if a timeline is wanted, that's a small backend addition to request, not
    something derivable client-side today), status update, delivery assignment.
11. **DeliveryAgent-only shell** (§4): My Deliveries (`GET .../orders/assigned-to-me`), My
    Status (`GET`/`PATCH .../delivery-agents/me`), status update on individual assigned orders.
12. **My Profile** (`GET`/`PUT /api/auth/me`).
