# Antivaly Shop Frontend Blueprint

**Self-contained.** This file assumes no other context — move it into the Antivaly project's
own repo and hand it to a fresh session; everything needed to build against the Vastora API is
here. If the Vastora API changes, this doc must be updated in the same session as the change
(see the intro note at the top of the main `VASTORA_BLUEPRINT.md` in the Vastora backend repo).

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
- **Password reset (added 2026-08-15, main blueprint §9.10):**
  `POST /api/shop/{slug}/auth/forgot-password` `{ email }` always 204s regardless of whether the
  email matches a Customer account on this Business — don't build a UI that reveals which.
  `POST /api/auth/reset-password` `{ token, newPassword }` (not slug-rooted — shared across
  every realm on the platform, the token itself identifies the account) completes it and
  revokes every active session for that customer. The email itself is a real branded HTML
  template now (added 2026-08-17, §9.10) — the Business's logo/name/brand color if this is a
  storefront account, generic Vastora branding for a BackOffice/Platform one — built around a
  working `{PublicBaseUrl}/reset-password?token=...` link, not a bare token to copy-paste.
  **Still no real delivery provider is configured by default** — until a deployment sets
  `Smtp:Host`, this (like every other email) only ever reaches the backend's own server log, so
  the flow isn't usable by a real customer yet; still worth building the UI, it's ready the day
  SMTP is configured.

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

**Raw input validation (empty required fields, malformed email) is enforced server-side** — a
global filter runs the backend's FluentValidation rules on every write before the action
executes (fixed 2026-08-15). Still validate registration/checkout forms client-side for
responsiveness, but the API itself now rejects bad input rather than accepting it silently.

---

## 6. API reference

Base URL = `NEXT_PUBLIC_API_BASE_URL`. `{slug}` = `NEXT_PUBLIC_BUSINESS_SLUG`.

### 6.1 Auth

| Method | Path | Auth | Body | Returns |
|---|---|---|---|---|
| POST | `/api/shop/{slug}/auth/register` | none | `StorefrontRegisterRequest` | `AuthResponse` |
| POST | `/api/shop/{slug}/auth/login` | none | `StorefrontLoginRequest` | `AuthResponse` |
| POST | `/api/shop/{slug}/auth/forgot-password` | none | `{ email }` | 204 |
| POST | `/api/auth/reset-password` | none | `{ token, newPassword }` | 204 |
| POST | `/api/auth/refresh` | none | `{ refreshToken }` | `AuthResponse` |
| POST | `/api/auth/logout` | none | `{ refreshToken }` | 204 |
| POST | `/api/auth/verify-email` | none | `{ token }` | 204 |
| POST | `/api/auth/resend-verification` | Customer | — | 204 |
| POST | `/api/auth/unsubscribe/{token}` | none | — | 204 — always, non-enumerating (§9.36) |
| GET | `/api/auth/me` | Customer | — | `UserSummaryResponse` |
| PUT | `/api/auth/me` | Customer | `{ fullName, phone }` | `UserSummaryResponse` |
| POST | `/api/auth/me/change-password` | Customer | `{ currentPassword, newPassword }` | 204 |
| POST | `/api/auth/me/avatar` | Customer | `multipart/form-data`, field `file` | `UserSummaryResponse` |
| DELETE | `/api/auth/me/avatar` | Customer | — | `UserSummaryResponse` |

**`/api/auth/*` is shared across every realm on the platform** (Customer, BackOffice staff, Platform), not Shop-specific — `GET`/`PUT /api/auth/me`, `change-password`, `avatar`, and the verify/resend/unsubscribe routes above are the same endpoints a BackOffice user hits, scoped by whichever JWT is presented. Only `register`/`login`/the storefront `forgot-password` are Business-slug-rooted and Customer-only (§4).

**Change password vs. reset password — two different flows for two different situations
(added 2026-08-17, main blueprint §9.41).** `POST /api/auth/me/change-password` is for a
signed-in customer who knows their current password and wants a new one — it verifies
`currentPassword` server-side and 401s if it's wrong. `POST /api/auth/reset-password` (§4) is
for a customer who's locked out and has no session — it trusts the emailed token instead. Both
end the same way: every active session is revoked, forcing a fresh login everywhere, including
the device that made the change. Build the "change password" form under Account settings against
the first one; don't reuse the forgot-password flow just because a session already exists.

**Avatar upload (added 2026-08-17, §9.41).** `POST /api/auth/me/avatar` takes a single
`multipart/form-data` file field named `file` — same 5 MB limit and
`image/jpeg`/`image/png`/`image/webp`/`image/gif` whitelist as product images. Local disk storage
today (`IFileStorageService`), so treat `avatarUrl` as relative to the API base URL, same as
`Business.logoUrl`/`bannerUrl`/product `images`. `DELETE /api/auth/me/avatar` clears it back to
`""`, at which point the frontend should fall back to a generated initial/placeholder avatar —
there's no default image server-side.

**Email verification, not phone.** `AppUser` has a `PhoneVerifiedAt` field in the data model but **no endpoint anywhere verifies a phone number** — don't build a "verify your phone" UI expecting one to exist. `resend-verification` re-sends the email link only; it 204s even if the account is already verified (idempotent, don't treat that as an error).

**Unsubscribe is login-free by design (§9.36).** `POST /api/auth/unsubscribe/{token}` takes `AppUser.UnsubscribeToken` (present on `CustomerDataExport`, and echoed as the last path segment of the unsubscribe link built into marketing emails — abandoned-cart, back-in-stock, review-request) and always 204s, valid token or not, so the link can never be used to probe for account existence. Land it on a simple confirmation page; there's nothing to display beyond "you're unsubscribed."

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
  phone: string; avatarUrl: string;               // added 2026-08-17, §9.41 — avatarUrl is "" when none set
  role: "Customer"; // will always be Customer for accounts created through this app
  tenantId: string; businessId: string;
  status: "PendingVerification" | "Active" | "Blocked";
  emailVerifiedAt: string | null; phoneVerifiedAt: string | null;  // phoneVerifiedAt is always null — see the note above, nothing ever sets it
  createdAt: string;                               // "member since"
};
```

### 6.2 Public catalog — no auth required

| Method | Path | Returns |
|---|---|---|
| GET | `/api/shop/{slug}` | `BusinessResponse` — landing page data |
| GET | `/api/shop/{slug}/categories` | `CategoryResponse[]` — active only, already filtered server-side |
| GET | `/api/shop/{slug}/products?...` | **`PagedResult<ProductResponse>`** — see the query below |
| GET | `/api/shop/{slug}/products/facets?...` | `CatalogFacetsResponse` — filter values + counts |
| GET | `/api/shop/{slug}/products/{productId}` | `ProductResponse`, or 404 if not `Active` |
| GET | `/api/shop/{slug}/products/{productId}/reviews?page=&pageSize=` | `PagedResult<ReviewResponse>` — published only |
| GET | `/api/shop/{slug}/products/{productId}/reviews/summary` | `ReviewSummaryResponse` — average + star histogram |
| POST | `/api/shop/{slug}/reviews/{reviewId}/helpful` | `ReviewResponse` — anonymous, no auth |
| GET | `/api/shop/{slug}/products/{productId}/also-bought` | `RecommendedProductResponse[]` — from real order history |
| GET | `/api/shop/{slug}/products/{productId}/related` | `RecommendedProductResponse[]` — same category fallback |
| GET | `/api/shop/{slug}/banners` | `ContentBlockResponse[]` — homepage slides, schedule already applied |
| GET | `/api/shop/{slug}/menu` | `MenuNode[]` — nav tree, one level of nesting |
| GET | `/api/shop/{slug}/pages` | `ContentBlockResponse[]` — About / Contact / Terms / Privacy |
| GET | `/api/shop/{slug}/pages/{pageSlug}` | `ContentBlockResponse`, 404 if unpublished or out of schedule |

> **⚠ Breaking change, 2026-08-16 (main blueprint §9.18).** `GET .../products` returns a paged
> envelope now, not a bare array. Read `.items`. Every list endpoint across the whole API
> changed the same way — see §6.5.

**Catalog query (§9.29).** All optional, all evaluated server-side:

```
?categoryId=&search=&minPrice=&maxPrice=&brand=&tags=a&tags=b
&inStockOnly=true&minRating=4&featuredOnly=true
&sort=Relevance|Newest|PriceAscending|PriceDescending|TopRated|BestSelling|NameAscending
&page=1&pageSize=24
```

`sort=Relevance` is the merchandising default: featured first, then the merchant's `sortWeight`,
then newest. Build the filter sidebar from `/products/facets` — it is computed over the *same*
filter as the listing, so its counts and the results can never disagree.

**`categoryId` matches the category *and every subcategory underneath it* (fixed 2026-08-18,
main blueprint §9.5) — build the category page around that, not around an exact match.**
`?categoryId=<Electronics.id>` returns products tagged `Electronics` directly **and** products
tagged `Phones`/`Laptops`/anything nested under it — a shopper browsing "Electronics" sees
everything in the department, the way any real storefront works, not just the handful of
products someone remembered to tag at the top level. Picking a specific subcategory
(`?categoryId=<Phones.id>`) narrows back down, since a leaf category has nothing under it to
expand into.

**The subcategory filter chips you'd want on a category page come from `/products/facets`,
free — no separate endpoint.** Because facets are computed over the same (now subcategory-
inclusive) filter as the listing, `GET .../products/facets?categoryId=<Electronics.id>`'s
`categories` array breaks down the *current* result set by each product's own category — in
practice, that means counts per subcategory (`{ value: "<Phones.id>", count: 12 }`,
`{ value: "<Laptops.id>", count: 5 }`, ...) the moment a parent category is selected. Resolve
those ids to names via `GET .../categories` or `.../categories/tree` (§6.2/BackOffice §7.3) to
render them as chips — clicking one is just navigating to `?categoryId=<that id>`.

```ts
type BusinessResponse = {
  id: string; tenantId: string; name: string; slug: string; customDomain: string | null;
  description: string; logoUrl: string; bannerUrl: string; themeColor: string;
  currency: string; contactEmail: string; contactPhone: string;
  status: "Draft" | "Active" | "Suspended";
  deliveryModuleEnabled: boolean;  // see note below
  defaultDeliveryFee: number;      // added 2026-08-15, main blueprint §9.7 — see §6.4
  createdAt: string;

  // --- added 2026-08-16 (§9B). Read these to decide what to render at all. ---
  tax: {
    enabled: boolean;
    defaultRatePercent: number;
    // TRUE means catalog prices already contain tax — show them as-is and label them
    // "incl. VAT". FALSE means tax is added at checkout; say so, or the total will surprise.
    pricesIncludeTax: boolean;
    taxShipping: boolean;
    classRates: Record<string, number>;
    registrationNumber: string;
    displayName: string;           // "VAT" | "GST" | "Sales Tax" — use this, don't hardcode
  };
  returnWindowDays: number;        // 0 = this shop accepts no returns; hide the returns UI
  reviewsEnabled: boolean;         // false = render no review UI at all
  guestCheckoutEnabled: boolean;   // false = require login before checkout
};
type CategoryResponse = {
  id: string; businessId: string; name: string; slug: string;
  parentCategoryId: string | null; description: string; imageUrl: string;
  sortOrder: number; isActive: boolean;
};
type ProductVariantResponse = {
  id: string; attributeSummary: string; sku: string; priceOverride: number | null; stockQuantity: number;
};
type ProductResponse = {
  id: string; businessId: string; categoryId: string; name: string; slug: string; sku: string;
  description: string; price: number; compareAtPrice: number | null;
  discountPercent: number | null; discountExpiresAt: string | null;
  effectivePrice: number; // ALWAYS display this as the price, not `price` — it already accounts for any active discount
  stockQuantity: number; trackInventory: boolean;
  reorderThreshold: number | null; reorderQuantity: number | null;  // BackOffice-facing fields, not customer-relevant — safe to ignore
  images: string[]; tags: string[];
  status: "Active"; // public endpoints only ever return Active products
  variants: ProductVariantResponse[];  // added 2026-08-15 (§9.5); BUYABLE since 2026-08-16 (§9.22) — see note below

  // --- added 2026-08-16 (§9.25, §9.28) ---
  averageRating: number;           // 0 when there are no published reviews yet
  reviewCount: number;
  brand: string;
  barcode: string;
  weightKg: number | null;
  metaTitle: string;               // fall back to `name` when empty
  metaDescription: string;         // fall back to a truncated `description` when empty
  publishedAt: string | null;
  unpublishedAt: string | null;
  isFeatured: boolean;
  sortWeight: number;
  taxClass: string;
  isAvailable: boolean;            // false = out of stock; prefer this over reading stockQuantity

  // ALWAYS null on public endpoints. The server strips cost data from the public projection —
  // if you ever see a number here you are calling a BackOffice route by mistake.
  costPrice: null;
  unitMargin: null;
};

// --- §9B response shapes ---
type PagedResult<T> = {
  items: T[];
  page: number; pageSize: number; totalCount: number; totalPages: number;
  hasNextPage: boolean; hasPreviousPage: boolean;
};
type CatalogFacetsResponse = {
  categories: { value: string; count: number }[];
  brands: { value: string; count: number }[];
  tags: { value: string; count: number }[];
  minPrice: number; maxPrice: number; inStockCount: number; totalCount: number;
};
type ReviewResponse = {
  id: string; productId: string; customerName: string; rating: number;  // 1–5
  title: string; body: string;
  status: "Pending" | "Published" | "Rejected";  // public reads only ever return Published
  isVerifiedPurchase: boolean;   // server-verified against real delivered orders — badge this
  merchantReply: string | null; merchantRepliedAt: string | null;
  helpfulCount: number; createdAt: string;
};
type ReviewSummaryResponse = {
  productId: string; averageRating: number; reviewCount: number;
  ratingCounts: Record<"1" | "2" | "3" | "4" | "5", number>;  // for the star histogram
};
type RecommendedProductResponse = {
  productId: string; productName: string; slug: string;
  effectivePrice: number; imageUrl: string | null; timesBoughtTogether: number;
};
type ContentBlockResponse = {
  id: string; type: "Banner" | "Page" | "MenuItem" | "Article";
  slug: string; title: string; subtitle: string; body: string;  // body is Markdown
  imageUrl: string; linkUrl: string; linkLabel: string;
  parentId: string | null; sortOrder: number; isPublished: boolean;
  startsAt: string | null; endsAt: string | null;
  metaTitle: string; metaDescription: string; isVisibleNow: boolean;
};
type MenuNode = { id: string; title: string; linkUrl: string; sortOrder: number; children: MenuNode[] };
```

**Variants are now buyable (§9.22).** If `variants` is non-empty the customer *must* pick one
before adding to cart — the API rejects a bare `productId` for such a product. Use the variant's
`priceOverride` when set (falling back to `effectivePrice`) and its own `stockQuantity`, which is
the authority; the product-level `stockQuantity` is a separate pool for variant-less products.

`search` is still a case-insensitive substring match against name/description/brand, evaluated
by MongoDB — no fuzzy matching, no typo tolerance, no relevance ranking. **It is also an
unanchored regex, which cannot use an index**, so it costs a collection scan per query. Fine at
Antivaly's scale; the backend flags Atlas Search as the fix if the catalog grows (§9.29). Prefer
the structured facet filters over free-text search wherever the UI allows it.

### 6.3 Cart — guests welcome since 2026-08-16, **not slug-rooted**

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/shop/cart?businessId=` | — | `CartResponse` |
| POST | `/api/shop/cart/items?businessId=&tenantId=` | `{ productId, quantity, variantId? }` | `CartResponse` |
| PUT | `/api/shop/cart/items/{productId}?businessId=` | `{ quantity, variantId? }` (0 removes) | `CartResponse` |
| DELETE | `/api/shop/cart/items/{productId}?variantId=&businessId=` | — | `CartResponse` |
| POST | `/api/shop/cart/coupon?businessId=` | `{ code }` | `CartResponse` |
| DELETE | `/api/shop/cart/coupon?businessId=` | — | `CartResponse` — added 2026-08-18, §9.45 |
| POST | `/api/shop/cart/promotions?businessId=` | `{ code }` | `CartResponse` — **stackable**, unlike the single coupon |
| DELETE | `/api/shop/cart/promotions/{code}?businessId=` | — | `CartResponse` |
| POST | `/api/shop/cart/gift-cards?businessId=` | `{ code }` | `CartResponse` — added 2026-08-18, §9.43 |
| DELETE | `/api/shop/cart/gift-cards/{code}?businessId=` | — | `CartResponse` — added 2026-08-18, §9.43 |
| PUT | `/api/shop/cart/store-credit` | `{ useStoreCredit }` | `CartResponse` — **Customer only**, added 2026-08-18, §9.43 |
| PUT | `/api/shop/cart/fulfillment-method?businessId=` | `{ fulfillmentMethod }` | `CartResponse` — Guest and Customer, added 2026-08-18, §9.44 |
| GET | `/api/shop/cart/available-offers?businessId=` | — | `AvailableOfferResponse[]` — added 2026-08-18, §9.43; see §6.6 |
| POST | `/api/shop/cart/merge?guestToken=` | — | `CartResponse` — **Customer only**, call right after login |
| DELETE | `/api/shop/cart?businessId=` | — | 204 (clears the whole cart) |

**Guest carts (§9.27).** The controller is anonymous now. Two identities exist:

- **Signed-in Customer** — send the JWT. `businessId`/`tenantId` come from the token; the query
  params are ignored. An authenticated identity always beats any cart token also present, so a
  stale token can never redirect a logged-in write to someone else's cart.
- **Guest** — send `X-Cart-Token: <token>` plus `?businessId=`. On the first write, omit the
  header: the server mints a token and returns it as `guestToken`. **Persist that in
  localStorage and send it on every subsequent cart and checkout call** — it is the only handle
  on that cart.

**On login or registration, call `POST /api/shop/cart/merge?guestToken=<token>` before anything
else.** Quantities are summed rather than replaced, and the guest cart is deleted. Skip this and
the shopper's pre-login cart is silently orphaned.

> **`DELETE /api/shop/cart/coupon`, added 2026-08-18 (§9.45).** This didn't exist before — only
> `POST` did — even though the promotions and gift-card endpoints both already had a matching
> `DELETE`. If you built a "remove coupon" action around clearing the cart and re-adding every
> item, or around some other workaround, switch it to this call; it only touches `couponCode`,
> nothing else on the cart.

```ts
type CartResponse = {
  id: string; businessId: string;
  items: {
    productId: string;
    variantId: string | null;      // §9.22 — the same product in two variants is two lines
    variantSummary: string | null; // e.g. "Red / Large", ready to render
    productName: string; unitPrice: number; quantity: number; lineTotal: number;
  }[];
  couponCode: string | null;
  promotionCodes: string[];
  subtotal: number;
  discounts: { source: "Coupon" | "Promotion"; label: string; amount: number; isFreeShipping: boolean }[];
  discountTotal: number;
  estimatedTotal: number;          // subtotal − discountTotal. Excludes shipping and tax on purpose.
  currency: string;
  itemCount: number;
  guestToken: string | null;       // present only for guest carts — store it
  // --- added 2026-08-18, §9.43 — see §6.6 ---
  giftCardCodes: string[];         // codes applied to the cart itself, not just at final checkout
  giftCardTotal: number;           // what those codes actually cover at the current total
  useStoreCredit: boolean;         // this cart's opt-in state, set via PUT .../store-credit
  storeCreditApplied: number;
  amountDue: number;               // subtotal − discountTotal + deliveryFee (tax still excluded — needs an address) − giftCardTotal − storeCreditApplied — the number checkout will actually collect
  // --- added 2026-08-18, §9.44 ---
  fulfillmentMethod: "Delivery" | "Pickup" | "ExternalCourier" | "Digital"; // this cart's preview setting, set via PUT .../fulfillment-method — defaults to "Delivery"
  deliveryFee: number;             // 0 whenever fulfillmentMethod is "Pickup" or "Digital" — see note below
  shippingMethodName: string | null;
  shippingOptions: {               // §9.20 — same shape checkout's shippingOptions uses; always empty for Pickup/Digital
    zoneId: string; rateId: string; name: string; price: number;
    estimatedDaysMin: number | null; estimatedDaysMax: number | null;
  }[];
};
```

> **The old caveat is gone.** The cart response used to return a bare `subtotal` with no way to
> show the discount, and this document told you to display "coupon applied: CODE10" without an
> amount. It is now priced by the same engine checkout uses, so `discounts` and `discountTotal`
> are real numbers you can render directly.

> **Gift cards and store credit used to be checkout-only, blind.** `giftCardCodes` could be set on
> the cart but were never priced back into this response — a shopper who applied one saw no
> discount until checkout actually charged them. `giftCardTotal`/`storeCreditApplied`/`amountDue`
> now come from the same pricing pass `discounts`/`discountTotal` always did (§9.43) — render them
> the same way, live, from `GET /api/shop/cart`, not just from `CheckoutPreviewResponse`.

`estimatedTotal` deliberately excludes delivery fee and tax: `estimatedTotal = subtotal −
discountTotal`, nothing else. Tax genuinely can't be known before a delivery address is — for the
full number including tax, call `POST /api/shop/orders/preview` (§6.4) once you have one, same as
before. **Delivery fee is different** — `deliveryFee` (added 2026-08-18, §9.44) *is* resolvable
before an address, from the business's shipping zones or its flat default rate, so it's its own
field rather than bundled silently into a total that would then change again. Render it
separately: `subtotal − discountTotal + deliveryFee` if you want a running total that includes it,
or just show the `deliveryFee` line item next to the discount lines you're already rendering.
`amountDue` (§9.43/§9.44) already includes it for you if all you want is one final number.

**Fulfillment method decides whether a delivery fee applies at all — set it before you show one.**
`fulfillmentMethod` defaults to `"Delivery"` on a fresh cart. Call
`PUT /api/shop/cart/fulfillment-method` the moment the shopper picks "Ship to me" vs "Pick up
in-store" (a toggle on the cart or checkout page, not a checkout-only field) so `deliveryFee`/
`shippingOptions` reflect the real choice immediately:
- **`"Delivery"` / `"ExternalCourier"`** — `deliveryFee` and `shippingOptions` are populated
  normally, exactly as before this field existed.
- **`"Pickup"` / `"Digital"`** — `deliveryFee` is always `0` and `shippingOptions` is always
  `[]`. **Don't render a delivery-fee row at all for these** rather than rendering "$0.00
  delivery" — `fulfillmentMethod` is what tells you *why* it's zero (no delivery is happening,
  not that delivery happens to be free). Hide the shipping-method selector entirely too; there's
  nothing in `shippingOptions` to pick from.

This is **preview-only**, mirroring how `useStoreCredit` already works (§6.6): setting it on the
cart makes `GET /api/shop/cart` honest about what will happen, but it does not by itself decide
what's charged. `POST /api/shop/orders/checkout`'s own `CheckoutRequest.fulfillmentMethod` (§6.4)
is what's actually billed — **send the same value there that you set on the cart**, or the
customer can see "Pickup, no delivery fee" through checkout and then be charged a delivery fee
anyway because the checkout call defaulted back to `"Delivery"`. The one thing `CheckoutRequest`
still requires unconditionally, even for `"Pickup"`, is `shippingAddress` — the API doesn't yet
let it go from a Pickup checkout, so keep collecting some address on that form regardless of which
fulfillment method is selected.

One cart per customer (or per guest token) per Business, server-persisted — safe to always `GET`
on page load rather than trusting local state, though caching the last response for snappy UI
updates between calls is fine.

### 6.4 Checkout & orders — guests welcome, **not slug-rooted**

| Method | Path | Auth | Body | Returns |
|---|---|---|---|---|
| POST | `/api/shop/orders/preview?businessId=` | none | `CheckoutRequest` | `CheckoutPreviewResponse` — **commits nothing** |
| POST | `/api/shop/orders/checkout?businessId=&tenantId=` | none | `CheckoutRequest` | `OrderResponse` |
| GET | `/api/shop/orders?page=&pageSize=` | Customer | — | `PagedResult<OrderResponse>` |
| GET | `/api/shop/orders/lookup?businessId=&orderNumber=&email=` | none | — | `OrderResponse` — guest tracking |
| GET | `/api/shop/orders/{orderId}` | Customer | — | `OrderResponse` |
| POST | `/api/shop/orders/{orderId}/cancel` | Customer | — | `OrderResponse` |
| POST | `/api/shop/orders/returns` | Customer | `CreateReturnRequest` | `ReturnResponse` |
| GET | `/api/shop/orders/returns?page=&pageSize=` | Customer | — | `PagedResult<ReturnResponse>` |
| POST | `/api/shop/orders/returns/{returnId}/cancel` | Customer | — | `ReturnResponse` |

**Call `preview` before showing a total.** It runs the identical pricing engine checkout uses —
promotions, coupon, shipping, tax, gift cards, store credit — and moves no stock, burns no coupon
usage and creates nothing. This is the only way to show a customer their real total before they
commit, and it cannot disagree with what they're eventually charged.

**Send an `Idempotency-Key` header on checkout (§9.17).** Any stable per-attempt string (a UUID
generated when the checkout page mounts). A retry with the same key replays the original response
and returns `Idempotency-Replayed: true` instead of placing a second order. Reusing the key with
a *different* body is a `409` — regenerate the key when the cart changes. Omitting the header is
allowed and keeps the old behaviour, but on a flaky mobile connection that means duplicate orders.

```ts
type CheckoutRequest = {
  shippingAddress: {
    label: string; line1: string; line2: string; city: string; state: string;
    postalCode: string; country: string; phone: string; isDefault: boolean;
  };
  deliveryFee?: number | null;      // optional since 2026-08-15 — see note below
  // --- all optional, added 2026-08-16 ---
  billingAddress?: typeof shippingAddress | null;  // defaults to the shipping address
  shippingRateId?: string | null;   // pick one of preview's `shippingOptions`; cheapest wins if omitted
  fulfillmentMethod?: "Delivery" | "Pickup" | "ExternalCourier" | "Digital";
  customerNote?: string | null;
  useStoreCredit?: boolean;         // Customer only; spends up to the available balance
  giftCardCodes?: string[] | null;
  // --- guest checkout only (§9.27); ignored when a Customer JWT is present ---
  guestEmail?: string | null;       // REQUIRED for a guest order
  guestPhone?: string | null;
  guestName?: string | null;
};

type CheckoutPreviewResponse = {
  subtotal: number;
  discounts: { source: string; label: string; amount: number; isFreeShipping: boolean }[];
  discountTotal: number;
  deliveryFee: number;
  shippingMethodName: string | null;
  shippingOptions: {                // §9.20 — render as selectable methods
    zoneId: string; rateId: string; name: string; price: number;
    estimatedDaysMin: number | null; estimatedDaysMax: number | null;
  }[];
  taxAmount: number;
  taxLines: { label: string; ratePercent: number; taxableAmount: number; taxAmount: number }[];
  pricesIncludeTax: boolean;        // TRUE = taxAmount is already inside `total`, don't add it again
  total: number;
  giftCardTotal: number;
  storeCreditAvailable: number;
  amountDue: number;                // what's left to pay after gift cards and store credit
  currency: string;
};

type CreateReturnRequest = {
  orderId: string;
  // desiredVariantId: CHANGED 2026-08-18 (§9.49) — required when resolution is "Exchange",
  // otherwise omit it. It's the variant of the SAME product this line is being swapped for.
  items: { productId: string; variantId: string | null; quantity: number; desiredVariantId?: string | null }[];
  reason: "Damaged" | "WrongItem" | "NotAsDescribed" | "ChangedMind" | "SizeOrFit" | "Other";
  reasonNote: string;
  resolution: "Refund" | "Exchange" | "StoreCredit";
};

type ReturnResponse = {
  id: string; rmaNumber: string; orderId: string; orderNumber: string; customerUserId: string;
  items: { productId: string; variantId: string | null; productName: string;
           quantity: number; unitPrice: number; lineRefund: number;
           desiredVariantId: string | null; desiredVariantSummary: string | null }[];  // CHANGED §9.49
  reason: CreateReturnRequest["reason"]; reasonNote: string;
  resolution: CreateReturnRequest["resolution"];
  // "Exchanged" added 2026-08-18 (§9.49) — the terminal state for an Exchange resolution,
  // distinct from "Refunded" since no money moved.
  status: "Requested" | "Approved" | "Rejected" | "Received" | "Refunded" | "Cancelled" | "Exchanged";
  requestedRefundAmount: number; approvedRefundAmount: number | null;
  currency: string; restocked: boolean; refundedAt: string | null;
  exchanged: boolean; exchangedAt: string | null;               // ADDED §9.49
  statusHistory: { status: ReturnResponse["status"]; timestamp: string; note: string }[];
  createdAt: string;
};
```

**Returns (§9.21).** Only `Delivered` orders can be returned, only within the Business's
`returnWindowDays` (read it from `BusinessResponse`; `0` means returns are off — hide the UI),
and only up to the quantity not already returned. Refunds are priced from the order's own
snapshot, so what the customer paid is what they get back. The customer can cancel while the
request is still `Requested` or `Approved`; after that the goods are in transit and staff own it.
A **partial** return leaves the order `Delivered` — it still is, for the items kept — and only
sets `refundedAmount`/`refundedQuantity`. Don't render "Refunded" off a non-zero `refundedAmount`.

**Exchange, actually implemented as of 2026-08-18 (§9.49) — previously `resolution: "Exchange"`
was accepted here but silently behaved exactly like `"Refund"`.** It's now real, but scoped:
**same product, same price, different variant only** — swap a size or color, nothing else. There
is no payment step, because there's no payment gateway in this system to collect a shortfall or
refund an overage, so:
- When the customer picks "Exchange" as the resolution, the return form must collect a
  `desiredVariantId` **per line** — the variant of that same product they want instead. Fetch the
  product's `variants` (from the product detail response) to build that picker; **only offer
  variants whose effective price matches what was actually paid** (`variant.priceOverride ??
  product's effective price` must equal the order line's `unitPrice`) — the server 409s on a
  mismatch, but filtering client-side avoids the round trip and explains itself better than an
  error would.
- Settlement is a separate staff action (`POST .../returns/{returnId}/exchange`, BackOffice-only —
  there's no customer-facing exchange-settlement call) that ships the new variant once the
  original is back. Nothing to build here beyond showing the resulting `"Exchanged"` status and
  the `desiredVariantSummary` on each item once it lands.

type OrderStatusEventResponse = { status: OrderResponse["status"]; timestamp: string; note: string };
type PaymentStatusEventResponse = { status: OrderResponse["paymentStatus"]; timestamp: string; note: string };
type OrderResponse = {
  id: string; businessId: string; orderNumber: string;
  customerUserId: string;             // "" for a guest order
  isGuestOrder: boolean;
  contactEmail: string; contactPhone: string;
  items: {
    productId: string; variantId: string | null; variantSummary: string | null;
    productName: string; unitPrice: number; quantity: number;
    refundedQuantity: number;         // §9.21 — how many of this line have come back
    lineTotal: number;
  }[];
  subtotal: number; couponCode: string | null; discountAmount: number;
  discounts: { source: string; label: string; amount: number }[];   // itemised, for the receipt
  deliveryFee: number;
  taxAmount: number; taxRatePercent: number;
  pricesIncludeTax: boolean;          // TRUE = taxAmount is inside `total`, not added to it
  total: number;
  giftCardTotal: number;
  giftCardsUsed: { codeSuffix: string; amountApplied: number }[];
  storeCreditApplied: number;
  amountDue: number;
  refundedAmount: number;
  currency: string;                   // snapshotted at checkout (§9.38) — display with this, not the Business's current currency
  status: "PendingPayment" | "Processing" | "Confirmed" | "OutForDelivery" | "Delivered" | "Cancelled" | "Refunded";
  paymentStatus: "Pending" | "Paid" | "Failed" | "Refunded";
  fulfillmentMethod: "Delivery" | "Pickup" | "ExternalCourier" | "Digital";
  shippingAddress: CheckoutRequest["shippingAddress"] | null;
  billingAddress: CheckoutRequest["shippingAddress"] | null;
  deliveryAgentUserId: string | null;
  shippingMethodName: string | null;
  carrierName: string | null; trackingNumber: string | null; trackingUrl: string | null;  // §9.20
  invoiceNumber: string | null;       // null until BackOffice issues one
  customerNote: string;
  statusHistory: OrderStatusEventResponse[];          // added 2026-08-15, main blueprint §9.7
  paymentStatusHistory: PaymentStatusEventResponse[];  // added 2026-08-15, main blueprint §9.6
  placedAt: string;
};
```

**Checkout reads whatever's currently in the shopper's server-side cart** — there's no
"items" field in `CheckoutRequest`; add everything to the cart first (§6.3), then checkout.
On success, the cart is cleared server-side automatically.

**Stock handling changed in 2026-08-16 (§9.17), and the change is in your favour.** Every line is
validated before any stock moves, each deduction is atomic and refuses to go below zero, and if a
later line fails, every deduction already made is put back. So a `409` naming a specific product
now genuinely means *nothing happened* — you can surface the message and let the shopper retry
without worrying that half their basket was silently reserved. Surface the message directly; it
names the product and the quantity actually available.

**Shipping is zone-aware now (changed 2026-08-16, main blueprint §9.20).** The old advice — one
flat fee per Business, compute anything smarter client-side — no longer applies. Precedence, most
to least specific: an explicit `deliveryFee` you pass, the `shippingRateId` the customer picked,
the cheapest matching zone rate, then the Business's flat `defaultDeliveryFee` as the fallback.

Call `preview` with the address to get `shippingOptions` (name, price, estimated days) and render
them as selectable methods. Never auto-upgrade the customer: with no `shippingRateId` the server
picks the *cheapest* match, deliberately. Free-shipping-over-X is expressed as a zero-priced band
in the merchant's rate table, so it just shows up as a £0 option — you don't special-case it.

**Tax (§9.19).** `taxAmount` on the order is snapshotted, as is `pricesIncludeTax`. When that flag
is `true`, the tax is already inside `total` — show it as "incl. {tax.displayName}" and **do not
add it again**, or you will display a total higher than the customer is charged. Label it with
`business.tax.displayName` ("VAT"/"GST"/"Sales Tax"), not a hardcoded word. Tax is computed on the
*discounted* base, so it moves when a promotion applies — another reason to render `preview`'s
numbers rather than computing your own.

**`deliveryModuleEnabled` on the storefront response (added 2026-08-15, main blueprint §9.14)**
tells you whether this Business runs an in-house delivery-agent workflow at all. As of 2026-08-16
there is a cleaner way to say what you mean: send `fulfillmentMethod: "Pickup"` or
`"ExternalCourier"` on the checkout request. A `false` `deliveryModuleEnabled` is still the signal
to hide delivery ETAs, but the order can now record *how* it is actually being fulfilled, and an
externally shipped order carries `carrierName`/`trackingNumber`/`trackingUrl` once staff fill
them in — render those on the order detail page as a tracking link.

> **Correction, 2026-08-18 (§9.44): `fulfillmentMethod: "Pickup"` did not actually drop the
> delivery fee before this date, despite the paragraph above.** It was recorded on the order as a
> label, but `deliveryFee` was still resolved from the shipping zones / `defaultDeliveryFee`
> exactly as if it were `"Delivery"` — a real billing bug, now fixed. No `CheckoutRequest` field
> changed and no frontend code needs to change for checkout itself; a Pickup order placed today
> correctly prices at `deliveryFee: 0`. What *is* new is the ability to preview it: see §6.3's
> `fulfillmentMethod`/`deliveryFee` on `CartResponse` and `PUT /api/shop/cart/fulfillment-method`
> — set that before checkout so the cart already shows `deliveryFee: 0` for a Pickup order,
> instead of a shopper only finding out at the final `preview`/`checkout` call.

**No payment gateway exists.** Checkout immediately creates the order with
`paymentStatus: "Pending"` and `status: "Processing"` — effectively a cash-on-delivery flow
today. Don't build a payment form expecting a gateway redirect/webhook; there's nothing on the
backend for it to talk to yet. **Business staff can now mark an order paid manually** (added
2026-08-15, main blueprint §9.6, a BackOffice-only action) — so `paymentStatus` can genuinely
change to `"Paid"` after checkout, just never automatically or from anything this app calls. If
a payment integration is needed before launch, that's backend work to request, not something
this app can fake convincingly.

**Order status now has a real state machine server-side (main blueprint §9.7)** — an order
predictably moves `Processing`→`Confirmed`→`OutForDelivery`→`Delivered`, or to `Cancelled` from
any of the pre-delivery states, or to `Refunded` only after `Delivered`. `statusHistory` (§6.4's
`OrderResponse`) now exposes the full timeline with timestamps — build the order-detail page's
tracking view directly from that array instead of just showing the current `status`.

**Cancellation** only works while `status` is `PendingPayment`, `Processing`, or `Confirmed` —
past that (`OutForDelivery` or later) the API returns `409` and the order can't be
self-cancelled by the customer. Restocks items automatically on success, and since 2026-08-16
also returns any gift-card value and store credit that was spent on it. After delivery the route
is a **return** (§9.21), not a cancellation.

**Order tracking is entirely `GET`-based — there's no push channel.** Everything a tracking page
needs lives on `OrderResponse` itself: `status`, `statusHistory` (the full timeline, §9.7),
`paymentStatus`/`paymentStatusHistory`, and `carrierName`/`trackingNumber`/`trackingUrl` once
staff add them. There is no separate `/track` or `/timeline` endpoint — fetch the order
(`GET .../orders/{orderId}` signed-in, or `GET .../orders/lookup?orderNumber=&email=` for a
guest) and build the whole page from that one response; re-poll it if a "refresh status" button
is wanted. **No SMS or push notification exists for order updates** — every status change is
email-only (best-effort, §9.10), so don't build a "text me updates" toggle;
`NotificationPreferences.marketingSms` exists as a data field but nothing sends to it yet.

### 6.5 New in 2026-08-16 — account surface

`/api/shop/account/*`, Customer only, scope entirely from the JWT.

| Method | Path | Returns |
|---|---|---|
| GET | `/api/shop/account/wishlist?page=&pageSize=` | `PagedResult<WishlistItemResponse>` |
| POST | `/api/shop/account/wishlist/{productId}` | `WishlistItemResponse` — idempotent, re-saving is a no-op |
| DELETE | `/api/shop/account/wishlist/{productId}` | 204 |
| POST | `/api/shop/account/reviews` | `ReviewResponse` — `{ productId, rating, title, body }` |
| GET | `/api/shop/account/store-credit` | `StoreCreditBalanceResponse` |
| GET | `/api/shop/account/gift-cards/{code}` | `GiftCardBalanceResponse` |
| GET | `/api/shop/account/data-export` | `CustomerDataExport` — offer as a JSON download |
| PUT | `/api/shop/account/notification-preferences` | 204 |
| DELETE | `/api/shop/account` | 204 — **irreversible**, confirm hard |
| GET | `/api/shop/account/addresses` | `AddressResponse[]` — added 2026-08-17, §9.41 |
| POST | `/api/shop/account/addresses` | `AddressResponse` — `SaveAddressRequest` body |
| PUT | `/api/shop/account/addresses/{addressId}` | `AddressResponse` — `SaveAddressRequest` body |
| DELETE | `/api/shop/account/addresses/{addressId}` | 204 |

```ts
type WishlistItemResponse = {
  id: string; productId: string; productName: string; slug: string;
  price: number; effectivePrice: number; imageUrl: string | null;
  inStock: boolean; addedAt: string;
};
type StoreCreditBalanceResponse = {
  balance: number; currency: string;   // balance already excludes any entry whose expiresAt has passed
  recentEntries: { id: string; amount: number; currency: string;
                   reason: "GiftCardRedemption" | "RefundToCredit" | "LoyaltyReward" | "ManualAdjustment" | "Spent";
                   note: string; referenceOrderId: string | null; createdAt: string;
                   expiresAt: string | null }[]; // added 2026-08-18, §9.43 — null means this entry never expires
};
type GiftCardBalanceResponse = {
  codeSuffix: string; remainingBalance: number; currency: string;
  expiresAt: string | null; isRedeemable: boolean;
};
type UpdateNotificationPreferencesRequest = {
  marketingEmail: boolean;      // opt-IN. Defaults false; only send marketing when true.
  backInStockAlerts: boolean;
  reviewRequests: boolean;
  marketingSms: boolean;
};

// --- added 2026-08-17, §9.41 ---
type AddressResponse = {
  id: string; label: string; line1: string; line2: string; city: string; state: string;
  postalCode: string; country: string; phone: string; isDefault: boolean;
};
type SaveAddressRequest = {
  label: string; line1: string; line2: string; city: string; state: string;
  postalCode: string; country: string; phone: string; isDefault: boolean;
};
```

**Reviews are moderated by default (§9.25).** A submitted review comes back `Pending` unless the
merchant enabled `autoPublishReviews` — tell the customer it's awaiting approval rather than
letting them refresh and wonder why it vanished. One review per customer per product; a second
attempt is a `409`. `isVerifiedPurchase` is established server-side against real delivered
orders and is worth badging.

**Wishlist doubles as the back-in-stock signal (§9.36).** Saving an out-of-stock product is how a
customer subscribes to its restock email — say so on the button when `isAvailable` is false.

**Account deletion anonymises rather than erases (§9.37).** Orders survive, stripped of personal
data, because the merchant has its own legal duty to retain them. Say that in the confirmation
dialog; "we'll delete everything" would be untrue.

**Saved address book, added 2026-08-17 (§9.41) — the gap flagged in the previous session is
closed.** `AddressResponse.id` is what makes an entry individually addressable for `PUT`/`DELETE`
— it's empty/meaningless on the one-off address embedded in an `Order` at checkout
(`OrderResponse.shippingAddress`/`billingAddress`, §6.4), only real on a saved book entry. Two
behaviors worth building UI around rather than re-deriving client-side:
- **The first address ever saved becomes the default automatically**, regardless of what
  `isDefault` was sent as — there is no "no default" state once at least one address exists. A
  simple "Save address" button on first use doesn't need its own default-toggle.
- **Deleting the current default promotes another one** (arbitrarily one of the rest) rather
  than leaving the book without a default. Don't assume "no default address" is reachable once
  the list is non-empty.

Still true, and still a frontend decision, not a backend gap: checkout itself is unchanged —
`CheckoutRequest.shippingAddress`/`billingAddress` still take an inline address every time
(§6.4). A "pick a saved address" control on the checkout form means fetching
`GET .../addresses` and pre-filling the inline fields from whichever one the shopper picks (the
one with `isDefault: true` is the sensible pre-selection) — the backend was never taught to
accept an address *id* at checkout, only a full address object.

### 6.6 Coupons, Gift Cards & Store Credit — added 2026-08-18, §9.43

Every discount feature in one place: coupon/promotion codes, gift cards, store credit, and how
they combine. Nothing here is a separate pricing engine — every one of these feeds the same
`IPricingService` pass that produces `CartResponse` (§6.3) and `CheckoutPreviewResponse` (§6.4),
so what you show pre-checkout and what the customer is actually charged cannot disagree.

**The four discount surfaces:**

| Surface | What it is | Applied via |
|---|---|---|
| Coupon | One legacy percentage/fixed code, single-use per cart | `POST/DELETE .../cart/coupon` (§6.3) |
| Promotion | Coded or automatic — BOGO, free shipping, scoped, stackable, group-targeted | `POST/DELETE .../cart/promotions[/{code}]` (§6.3) |
| Gift card | Stored balance, spent down at checkout | `POST/DELETE .../cart/gift-cards[/{code}]` (below) |
| Store credit | Per-customer ledger balance (refunds, promotional grants) | `PUT .../cart/store-credit` (below) |

A coupon and any stacked promotions apply first, against the subtotal; gift cards and store
credit apply last, against the post-tax total. See §6.4's `CheckoutPreviewResponse` for the full
order of operations.

#### Showing codes where applicable — `GET /api/shop/cart/available-offers?businessId=`

The industry-grade pattern this replaces "does the customer already know the code" with: a code
is either **discoverable** (list it — a banner, an "Available offers" panel at checkout) or
**targeted** (never list it — the customer only learns it exists because you told them, e.g. by
email). That's the `Public`/`Hidden` visibility every `Coupon` and coded `Promotion` now carries.

```ts
type AvailableOfferResponse = {
  source: "Coupon" | "Promotion";
  code: string;
  label: string;               // the promotion's Name, or the coupon code itself
  summary: string;              // server-formatted: "10% off", "$5 off orders over $50", "Free shipping", "Buy 2, get 1 free"
  minOrderAmount: number | null;
  expiresAt: string | null;     // null = never expires
};
```

Call this whenever the cart changes (or at minimum on the cart/checkout page mount) and render
each offer with an "Apply" action that calls the matching `POST .../cart/coupon` or
`.../cart/promotions` endpoint. **Only `Public` codes appear here.** A `Hidden` code — the one a
customer received by email, not by browsing — is deliberately absent from this list; it still
applies normally through the exact same `POST` endpoints when the customer types it in. Don't
build a second "enter a code" input for Hidden codes — there's only ever one apply flow, this
endpoint just decides what to suggest before the customer types anything.

This is a shortlist, not a guarantee: it filters on `minOrderAmount` against the cart's current
subtotal, but a code can still fail to apply (wrong customer segment, first-order-only, etc.) —
handle the `409` from the apply call the same way you already do for a plain wrong/expired code.

> **Correction, 2026-08-18 (§9.46): a customer-group-targeted or first-order-only code was
> rejected for *every* customer, eligible or not, before this date.** `POST /api/shop/cart/
> promotions` checked eligibility against the wrong customer state internally — not against
> whether *this* shopper actually qualified. If you built a workaround (hiding these promo types,
> a retry loop, treating the `409` as always-expected for such codes), remove it; the endpoint now
> checks the real customer, same as the rest of pricing always did. No request/response shape
> changed — same body, same `CartResponse`, same `409` shape for a genuinely ineligible customer.

#### Gift cards on the cart — `POST/DELETE /api/shop/cart/gift-cards[/{code}]?businessId=`

```ts
// POST body: { code: string }   →  CartResponse
// DELETE /api/shop/cart/gift-cards/{code}?businessId=   →  CartResponse
```

Same shape as applying a coupon (§6.3): the code is validated against the gift-card ledger
immediately — an unknown, inactive, zero-balance, or expired code is rejected with a `409` right
here, not silently accepted and discovered wrong at checkout. A cart can hold several gift-card
codes at once; `CartResponse.giftCardCodes`/`giftCardTotal` (§6.3) reflect all of them combined.
Checking a code's balance without applying it is still `GET /api/shop/account/gift-cards/{code}`
(§6.5, Customer only) — use that for a "check your balance" utility separate from checkout.

#### Store credit on the cart — `PUT /api/shop/cart/store-credit`

```ts
// body: { useStoreCredit: boolean }   →  CartResponse
```

**Customer only** — a guest has no account and therefore no balance to opt into. A simple
checkbox: "Use my store credit ($`balance`)". Toggling it on prices the cart with as much of the
customer's live balance applied as the remaining total allows (`CartResponse.storeCreditApplied`,
capped automatically — never send an amount, just the boolean); toggling it off removes it from
the preview immediately. `GET /api/shop/account/store-credit` (§6.5) is still where the balance
and ledger history come from.

#### Expiry — what "expires" actually means for each surface

| Surface | Expiry field | `null` means |
|---|---|---|
| Coupon | `expiresAt` on the coupon itself | Never expires (an evergreen/partner code — added 2026-08-18; previously every coupon was forced to carry a date) |
| Promotion | `endsAt` | Never expires |
| Gift card | `expiresAt` on the card | Never expires |
| Store credit entry | `expiresAt` on the ledger entry | This credit never expires (the default for a refund settlement; a promotional grant can set a real date) |

A store-credit balance already **excludes** any lapsed entry — `GET /api/shop/account/store-credit`
computes it live, so there is nothing for the frontend to filter. If you want to show "$10 of your
credit expires in 5 days" as a nudge, read it off `recentEntries[].expiresAt` yourself; the API
doesn't synthesize that message for you.

---

## 7. Recommended pages

1. **Home / Landing** — `GET /api/shop/{slug}` for hero/branding + a products/categories
   pull for featured sections. Good SSG/ISR candidate.
2. **Category / product listing** — `GET /api/shop/{slug}/products` with the §6.2 catalog query,
   a facet sidebar from `/products/facets`, a sort dropdown, and pagination off the envelope.
   SSG/ISR candidate, revalidate short-interval.
3. **Product detail** — `GET /api/shop/{slug}/products/{productId}`. SSG/ISR candidate (or
   SSR if wanting always-fresh stock counts — `stockQuantity` can go stale under ISR).
4. **Register / Login** — `POST /api/shop/{slug}/auth/register` / `.../login`.
5. **Forgot / reset password** (added 2026-08-15, §4) — worth building even though real email
   delivery doesn't exist yet, so it's ready once a provider is chosen backend-side.
6. **Cart** — full CRUD per §6.3, client-rendered (needs auth).
7. **Checkout** — address form → `POST /api/shop/orders/preview` on every address or method
   change → `POST /api/shop/orders/checkout` with an `Idempotency-Key`. Show the `409` stock
   message inline against the offending item if checkout fails. Works for guests; prompt for an
   email rather than forcing registration when `guestCheckoutEnabled` is true.
8. **Order history** — `GET /api/shop/orders` list, `GET .../{orderId}` detail with a real
   status timeline built from `statusHistory` (§6.4), cancel action gated by `status` per
   §6.4's note, a tracking link when `trackingUrl` is set, and a "return items" action for
   `Delivered` orders inside the return window.
9. **Account / profile** — `GET`/`PUT /api/auth/me` for name/phone, `POST/DELETE
   /api/auth/me/avatar` for the profile picture, `POST /api/auth/me/change-password` for an
   in-session password change (§6.1), a saved-addresses tab against `/api/shop/account/addresses`
   (§6.5), plus the rest of the §6.5 surface (wishlist, reviews, store credit, gift cards,
   notification preferences, data export, delete account).
10. **Guest order tracking** — a public page taking order number + email against
   `GET /api/shop/orders/lookup`. Link it from the confirmation email; a guest has no account
   to log into.
11. **Wishlist** (§6.5) and **Returns** (§6.4) — both need their own pages.
12. **Static pages** — render `GET /api/shop/{slug}/pages/{pageSlug}` as Markdown. Terms and
   Privacy are legally required in most markets and are merchant-authored, not yours to write.
13. **Email verification landing** — `POST /api/auth/verify-email` with the token from the link.
   Build it even though `Auth:RequireEmailVerification` currently defaults off, so the shop is
   ready the day a real email provider is wired in.

---

## 8. Notes for whoever implements this

- `EffectivePrice` (not `Price`) is what a customer should ever see as "the price" —
  `Price`/`CompareAtPrice` exist for showing a strikethrough original price alongside a
  discount, not as the number to charge.
- **Three notes that used to live here are now obsolete** (all resolved 2026-08-16): reviews and
  ratings exist (§6.2, §6.5), a wishlist exists (§6.5), and guest checkout exists (§6.3, §6.4).
  If you find advice elsewhere in this document that contradicts those, this section wins.
- **Every list endpoint returns a paged envelope, not an array** (§9.18). Read `.items`. This is
  the single most likely thing to break a client written against the older contract.
- **Never compute a total yourself.** `POST /api/shop/orders/preview` runs the exact pricing
  engine checkout uses — promotions, coupon, group pricing, shipping, tax, gift cards, credit.
  Any number you derive independently will eventually disagree with what the customer is charged.
- **Read `currency` off the order, not off the Business.** It is snapshotted at checkout (§9.38),
  so a merchant changing currency later doesn't retroactively relabel old orders.
- **`costPrice` and `unitMargin` are always `null` on public endpoints.** If you ever see numbers
  there, you're calling a BackOffice route by mistake — that's the merchant's supplier pricing.
- **Still no payment gateway** (§9.6). Checkout is cash-on-delivery-shaped; `amountDue` tells you
  what's left after gift cards and store credit, but nothing collects it online.
- **Still no real delivery provider configured by default** (§9.10). Every transactional and
  lifecycle email — verification, password reset, order confirmation/status/shipping, returns/
  refunds, abandoned cart, back-in-stock, review requests — now renders as a real branded HTML
  template (added 2026-08-17) with the Business's logo, name, and brand color, alongside a
  plain-text fallback. It's genuinely sendable SMTP mail (`SmtpNotificationService`, MailKit) —
  but until a deployment sets `Smtp:Host`, it only ever reaches the backend's own server log
  (`LoggingNotificationService`). Build every flow that depends on receiving one of these; they
  work end-to-end once SMTP is configured, they just don't leave the server today.
