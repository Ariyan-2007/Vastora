# Antivaly Shop Frontend Blueprint

**Self-contained.** This file assumes no other context — move it into the Antivaly project's
own repo and hand it to a fresh session; everything needed to build against the Vastora API is
here. If the Vastora API changes, this doc must be updated in the same session as the change
(see the main `VASTORA_BLUEPRINT.md`, §10, in the Vastora backend repo).

---

## 1. What this app is

**Antivaly** is the public Landing Page + Shop + customer account experience for **one
Business** on Vastora — the first real client onboarded onto the platform (the name is a nod
to the original single-shop project Vastora's backend generalized from; see the main
blueprint's §8 if curious, not relevant to building this app). Anyone can browse it; an
account is only needed to buy.

### Where it sits in the platform

Vastora is a multi-tenant e-commerce API platform. Three tiers:

```
Platform (Vastora itself — not this app)
  └─ Tenant (the paying subscriber that owns the Antivaly Business)
       └─ Business "Antivaly" (this app is its public Landing Page + Shop)
```

Antivaly's staff manage products/orders/etc. through a *separate* BackOffice app (different
repo, different login realm) — this app is customer-facing only: browsing, cart, checkout,
order history, account.

---

## 2. Tech stack

- **Next.js** (App Router), TypeScript.
- Public browsing pages (home, category/product listing, product detail) are good candidates
  for Server Components / SSG+ISR against the public catalog endpoints (§5) — they need no
  auth and benefit from SEO/fast first paint. Revalidate on a short interval (e.g. 60s) or
  on-demand if a webhook/rebuild hook is ever added for catalog changes.
- Cart/checkout/account/order-history are inherently per-user and require an auth token —
  build those as Client Components hitting the API directly from the browser (or via Next.js
  Route Handlers acting as a thin proxy, if hiding the API base URL from the client is wanted
  — not required, just an option).
- State: whatever's comfortable (Zustand/Context) for cart state if not solely relying on the
  server-side cart (see §6.3 — the server already persists the cart per logged-in customer,
  so client state can just be a thin cache of the last-fetched `CartResponse`).

---

## 3. Environment configuration

```
NEXT_PUBLIC_API_BASE_URL=https://api.vastora.app
NEXT_PUBLIC_BUSINESS_SLUG=antivaly
```

`NEXT_PUBLIC_BUSINESS_SLUG` is the exact slug returned as `business.slug` when this Business
was provisioned on Vastora (via the Tenant sign-up call — get it from whoever ran onboarding).
Every public catalog call is rooted at `/api/shop/{NEXT_PUBLIC_BUSINESS_SLUG}/...`; every
authenticated customer call (cart/orders/profile) is *not* slug-rooted — see §5, the JWT alone
scopes those once the customer is logged in.

If this codebase is ever reused as a template for a second client's shop (plausible later, even
though it isn't the ask right now), this is the value that would change — but scaffolding it
as a reusable template isn't in scope for this build. Get it right for Antivaly first.

---

## 4. Auth & token strategy

**This app's login/register realm is Business-scoped by slug** — different from the
BackOffice/SuperOffice shared realm:

- `POST /api/shop/{slug}/auth/register` — new customer, this Business only.
- `POST /api/shop/{slug}/auth/login` — existing customer, this Business only.

Both return the same `AuthResponse` shape as everywhere else in Vastora (§5.1). After login,
**every other endpoint this app calls is not slug-rooted** — `/api/shop/cart`,
`/api/shop/orders`, `/api/auth/me` all resolve which Business/customer via the JWT alone. Only
registration/login and public catalog browsing use the `{slug}` in the path.

- Access token ~30 min. Refresh token 30 days, **rotates on every use** — persist the newest
  one after every refresh call.
- `POST /api/auth/refresh` with `{ refreshToken }`, `POST /api/auth/logout` with the same.

**Storage:** for a Next.js app, prefer storing tokens in an httpOnly cookie set by a Next.js
Route Handler that proxies the login/refresh calls, rather than `localStorage` — this app is
public-facing with real customer accounts and payment-adjacent flows (even though there's no
payment gateway wired up yet, see §6.5), so it's worth doing better than the pragmatic
`localStorage` shortcut used in the two internal BackOffice/SuperOffice apps. If that's too
much upfront complexity, `localStorage` still works and matches the rest of the platform's
current foundation-phase posture — just know it's the weaker option.

Anonymous browsing (home, listing, product detail) needs no token at all.

---

## 5. Error handling contract

RFC 7807 `application/problem+json` everywhere:

```json
{ "status": 404, "title": "...", "type": "https://httpstatuses.io/404", "errors": { "field": ["msg"] } }
```

`errors` only on `400`. `401` = not logged in / expired token (redirect to login for
account/cart/order actions; public pages never hit this). `404` = not found (a bad product ID,
or the Business slug itself being wrong — see §3's misconfiguration note). `409` = conflict —
this is the one customers will see in normal use: email already registered, coupon invalid,
stock too low at checkout ("`'Product X' only has 3 left in stock.`" — the message text is
returned in `title`, safe to show directly to the customer for this specific error type).
`500` unexpected.

**Known gap:** raw input validation (empty required fields, malformed email) isn't fully
enforced server-side yet for every endpoint — validate registration/checkout forms client-side
defensively.

---

## 6. API reference

Base URL = `NEXT_PUBLIC_API_BASE_URL`. `{slug}` = `NEXT_PUBLIC_BUSINESS_SLUG`.

### 6.1 Auth

| Method | Path | Auth | Body | Returns |
|---|---|---|---|---|
| POST | `/api/shop/{slug}/auth/register` | none | `StorefrontRegisterRequest` | `AuthResponse` |
| POST | `/api/shop/{slug}/auth/login` | none | `StorefrontLoginRequest` | `AuthResponse` |
| POST | `/api/auth/refresh` | none | `{ refreshToken }` | `AuthResponse` |
| POST | `/api/auth/logout` | none | `{ refreshToken }` | 204 |
| GET | `/api/auth/me` | Customer | — | `UserSummaryResponse` |
| PUT | `/api/auth/me` | Customer | `{ fullName, phone }` | `UserSummaryResponse` |

```ts
type StorefrontRegisterRequest = { fullName: string; email: string; password: string; phone: string };
type StorefrontLoginRequest = { email: string; password: string };

type AuthResponse = {
  accessToken: string; accessTokenExpiresAt: string;
  refreshToken: string; refreshTokenExpiresAt: string;
  user: UserSummaryResponse;
};
type UserSummaryResponse = {
  id: string; fullName: string; email: string;
  role: "Customer"; // will always be Customer for accounts created through this app
  tenantId: string; businessId: string;
  status: "PendingVerification" | "Active" | "Blocked";
};
```

### 6.2 Public catalog — no auth required

| Method | Path | Returns |
|---|---|---|
| GET | `/api/shop/{slug}` | `BusinessResponse` — landing page data |
| GET | `/api/shop/{slug}/categories` | `CategoryResponse[]` — active only, already filtered server-side |
| GET | `/api/shop/{slug}/products?categoryId=&search=` | `ProductResponse[]` — both query params optional |
| GET | `/api/shop/{slug}/products/{productId}` | `ProductResponse`, or 404 if not `Active` |

```ts
type BusinessResponse = {
  id: string; tenantId: string; name: string; slug: string; customDomain: string | null;
  description: string; logoUrl: string; bannerUrl: string; themeColor: string;
  currency: string; contactEmail: string; contactPhone: string;
  status: "Draft" | "Active" | "Suspended"; createdAt: string;
};
type CategoryResponse = {
  id: string; businessId: string; name: string; slug: string;
  parentCategoryId: string | null; description: string; imageUrl: string;
  sortOrder: number; isActive: boolean;
};
type ProductResponse = {
  id: string; businessId: string; categoryId: string; name: string; slug: string; sku: string;
  description: string; price: number; compareAtPrice: number | null;
  discountPercent: number | null; discountExpiresAt: string | null;
  effectivePrice: number; // ALWAYS display this as the price, not `price` — it already accounts for any active discount
  stockQuantity: number; trackInventory: boolean;
  images: string[]; tags: string[];
  status: "Active"; // public endpoints only ever return Active products
};
```

`search` does a simple case-insensitive substring match against product name/description —
no fuzzy matching, no relevance ranking. Fine for a small catalog; revisit if Antivaly's
catalog grows large (backend roadmap flags this as a known scaling limit).

### 6.3 Cart — Customer only, **not slug-rooted**

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/shop/cart` | — | `CartResponse` |
| POST | `/api/shop/cart/items` | `{ productId, quantity }` | `CartResponse` |
| PUT | `/api/shop/cart/items/{productId}` | `{ quantity }` (0 removes the item) | `CartResponse` |
| DELETE | `/api/shop/cart/items/{productId}` | — | `CartResponse` |
| POST | `/api/shop/cart/coupon` | `{ code }` | `CartResponse` |
| DELETE | `/api/shop/cart` | — | 204 (clears the whole cart) |

```ts
type CartResponse = {
  id: string; businessId: string;
  items: { productId: string; productName: string; unitPrice: number; quantity: number; lineTotal: number }[];
  couponCode: string | null;
  subtotal: number; // sum of lineTotal — does NOT subtract the coupon; compute the discounted total client-side if needed, or wait for checkout (§6.4) which returns the real total
};
```

One cart per customer per Business, server-persisted — safe to just always `GET` on page load
rather than trusting local state alone, though caching the last response for snappy UI updates
between calls is fine.

Adding a coupon here only *validates and stores the code* on the cart — the actual discount
amount is computed at checkout (§6.4), not returned by this endpoint. Don't try to display a
"discounted subtotal" from the cart response alone; either compute it client-side using the
same rule the coupon implies (percentage/fixed — not returned by this endpoint either, so
realistically just show "coupon applied: CODE10" without a computed amount until checkout).

### 6.4 Checkout & orders — Customer only, **not slug-rooted**

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `/api/shop/orders/checkout` | `CheckoutRequest` | `OrderResponse` |
| GET | `/api/shop/orders` | — | `OrderResponse[]` (own orders, newest first) |
| GET | `/api/shop/orders/{orderId}` | — | `OrderResponse` |
| POST | `/api/shop/orders/{orderId}/cancel` | — | `OrderResponse` |

```ts
type CheckoutRequest = {
  shippingAddress: {
    label: string; line1: string; line2: string; city: string; state: string;
    postalCode: string; country: string; phone: string; isDefault: boolean;
  };
  deliveryFee: number; // caller-supplied — no zone/distance calculation exists server-side yet, see note below
};

type OrderResponse = {
  id: string; businessId: string; orderNumber: string; customerUserId: string;
  items: { productId: string; productName: string; unitPrice: number; quantity: number; lineTotal: number }[];
  subtotal: number; couponCode: string | null; discountAmount: number; deliveryFee: number; total: number;
  status: "PendingPayment" | "Processing" | "Confirmed" | "OutForDelivery" | "Delivered" | "Cancelled" | "Refunded";
  paymentStatus: "Pending" | "Paid" | "Failed" | "Refunded";
  shippingAddress: CheckoutRequest["shippingAddress"] | null;
  deliveryAgentUserId: string | null;
  placedAt: string;
};
```

**Checkout reads whatever's currently in the customer's server-side cart** — there's no
"items" field in `CheckoutRequest`; add everything to the cart first (§6.3), then checkout.
On success, the cart is cleared server-side automatically. Stock is decremented per item at
checkout time (for `trackInventory` products); if any item's stock is insufficient, the whole
checkout fails with a `409` naming the specific product (§5) — surface that message directly.

**`deliveryFee` is entirely client/caller-supplied today** — there is no delivery-zone or
distance-based fee calculation on the backend yet. Antivaly needs to decide its fee logic
(flat rate? free above a threshold?) and compute it client-side before calling checkout; the
backend just records whatever number is sent. This is a known simplification, not a bug to
work around cleverly — flag it if a fee calculator ends up needing server-side logic.

**No payment gateway exists.** Checkout immediately creates the order with
`paymentStatus: "Pending"` and `status: "Processing"` — effectively a cash-on-delivery flow
today. Don't build a payment form expecting a gateway redirect/webhook; there's nothing on the
backend for it to talk to yet. If a payment integration is needed before launch, that's
backend work to request, not something this app can fake convincingly.

**Cancellation** only works while `status` is `PendingPayment`, `Processing`, or `Confirmed` —
past that (`OutForDelivery` or later) the API returns `409` and the order can't be
self-cancelled by the customer. Restocks items automatically on success.

---

## 7. Recommended pages

1. **Home / Landing** — `GET /api/shop/{slug}` for hero/branding + a products/categories
   pull for featured sections. Good SSG/ISR candidate.
2. **Category / product listing** — `GET /api/shop/{slug}/products?categoryId=&search=`,
   with category filter chips from `GET /api/shop/{slug}/categories`. SSG/ISR candidate,
   revalidate short-interval.
3. **Product detail** — `GET /api/shop/{slug}/products/{productId}`. SSG/ISR candidate (or
   SSR if wanting always-fresh stock counts — `stockQuantity` can go stale under ISR).
4. **Register / Login** — `POST /api/shop/{slug}/auth/register` / `.../login`.
5. **Cart** — full CRUD per §6.3, client-rendered (needs auth).
6. **Checkout** — address form → `POST /api/shop/orders/checkout`. Show the `409` stock
   message inline against the offending item if checkout fails.
7. **Order history** — `GET /api/shop/orders` list, `GET .../{orderId}` detail, cancel action
   gated by `status` per §6.4's note.
8. **Account / profile** — `GET`/`PUT /api/auth/me`.

---

## 8. Notes for whoever implements this

- `EffectivePrice` (not `Price`) is what a customer should ever see as "the price" —
  `Price`/`CompareAtPrice` exist for showing a strikethrough original price alongside a
  discount, not as the number to charge.
- No product reviews/ratings exist in the API — don't design a rating widget around data that
  doesn't exist yet.
- No wishlist/favorites endpoint exists.
- Guest checkout does not exist — an account is required before anything in §6.3/§6.4 works
  (all Customer-role-gated). Design the cart page to prompt login/register rather than
  supporting an anonymous cart, since the backend has nowhere to persist one.
