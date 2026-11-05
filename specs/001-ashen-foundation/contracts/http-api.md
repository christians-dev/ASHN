# HTTP API Contract (BFF)

All endpoints are under `/api` on the same origin as the SPA. JSON, camelCase. Money is
`{ "amount": 12.50, "currency": "USD" }`. Errors are RFC 9457 problem details
(`application/problem+json`) with `errors` for validation failures. The OpenAPI document is
generated at `/openapi/v1.json` (development) and used to generate the SPA's TypeScript types.

**Auth column**: `Anon` = anyone, `Cust` = signed-in (any role), `Admin` = Admin role.
All `POST/PUT/PATCH/DELETE` require the `X-XSRF-TOKEN` header (value from the `XSRF-TOKEN` cookie
issued by `GET /api/auth/session`). Lists use `?page=1&pageSize=24` and return
`{ items, page, pageSize, totalCount }`.

## Auth & session (Identity)

| Method | Path | Auth | Purpose / FR |
|---|---|---|---|
| GET | `/auth/session` | Anon | Current user `{ isAuthenticated, userId, email, displayName, roles[] }`; issues antiforgery cookie |
| POST | `/auth/register` | Anon | `{ email, password, displayName }` → signs in (FR-007, FR-010). Rate limited |
| POST | `/auth/login` | Anon | `{ email, password }` → cookie; 423-style problem on lockout (FR-011). Rate limited |
| POST | `/auth/logout` | Cust | Ends session |
| GET | `/auth/google?returnUrl=` | Anon | Starts Google sign-in (redirect) (FR-007) |
| GET | `/auth/google/callback` | Anon | Google returns here; links/creates account (FR-008); redirects to `returnUrl` (local paths only) |
| POST | `/auth/password/forgot` | Anon | `{ email }` → always 202 (no account enumeration) (FR-009) |
| POST | `/auth/password/reset` | Anon | `{ email, token, newPassword }` |
| GET/PUT | `/account` | Cust | View/edit name and email (FR-015) |
| DELETE | `/account` | Cust | Request account deletion (FR-015) |
| GET | `/admin/admins` | Admin | List admins (FR-014) |
| POST | `/admin/admins` | Admin | `{ email }` grant Admin to existing account |
| DELETE | `/admin/admins/{userId}` | Admin | Revoke; 409 if last admin |

## Catalog

| Method | Path | Auth | Purpose / FR |
|---|---|---|---|
| GET | `/home` | Anon | Featured products (ordered) + categories (FR-006) |
| GET | `/categories` | Anon | All categories |
| GET | `/products?category=&q=&size=&colour=&minPrice=&maxPrice=&sort=newest\|price_asc\|price_desc\|rating` | Anon | Published products: summary with primary image, from-price, rating (FR-001/002) |
| GET | `/products/{slug}` | Anon | Detail: images, `options` (`[{ name, values[] }]`), variants with `optionValues` and `isAvailable`, rating summary; 404 if not published (Story 1 #4) |
| GET | `/admin/products?status=` | Admin | All products incl. drafts |
| GET | `/admin/products/{id}` | Admin | One product for the editor: options, variants with stock, images, status |
| POST | `/admin/products` | Admin | Create draft (FR-042) |
| PUT | `/admin/products/{id}` | Admin | Edit name, description, category |
| POST | `/admin/products/{id}/publish` · `/unpublish` · `/archive` | Admin | Status changes; DELETE on an ordered product → archive (FR-044) |
| DELETE | `/admin/products/{id}` | Admin | Hard delete only if never ordered, else archives |
| PUT | `/admin/products/{id}/options` | Admin | `{ options: [{ name, values[] }] }` (1–3); 409 if it would add/remove an option while variants exist or remove a value in use |
| POST | `/admin/products/{id}/variants` | Admin | Add variant `{ sku, optionValues, price, stockOnHand }`; 400 if values don't match the product's options; 409 on duplicate combination |
| PUT | `/admin/products/{id}/variants/{variantId}` | Admin | Edit variant price/option values (same rules) |
| PUT | `/admin/products/{id}/variants/{variantId}/stock` | Admin | `{ stockOnHand }` (FR-045) |
| POST | `/admin/products/{id}/images` | Admin | multipart upload (FR-043) |
| PUT | `/admin/products/{id}/images/order` | Admin | `{ imageIds[] }` |
| DELETE | `/admin/products/{id}/images/{imageId}` | Admin | |
| PUT | `/admin/featured` | Admin | `{ productIds[] }` in display order |
| POST/PUT/DELETE | `/admin/categories[/{id}]` | Admin | Create/rename/delete-if-empty |
| GET | `/media/{path}` | Anon | Product images (static, not under `/api`) |

## Customers

| Method | Path | Auth | Purpose / FR |
|---|---|---|---|
| GET/POST | `/me/addresses` | Cust | List/add (FR-016/017) |
| PUT/DELETE | `/me/addresses/{id}` | Cust | Edit/delete |
| POST | `/me/addresses/{id}/default` | Cust | Set default |
| GET | `/me/wishlist` | Cust | Items with product summary + availability (FR-041) |
| PUT/DELETE | `/me/wishlist/{productId}` | Cust | Add (idempotent)/remove |

## Payments

| Method | Path | Auth | Purpose / FR |
|---|---|---|---|
| GET | `/me/cards` | Cust | Saved cards: brand, last4, expiry, isDefault, isExpired (FR-029) |
| POST | `/me/cards` | Cust | `{ number, expMonth, expYear, cvc }` → tokenized via gateway; number/cvc discarded (FR-027) |
| POST | `/me/cards/{id}/default` · DELETE `/me/cards/{id}` | Cust | |
| GET | `/payments/mode` | Anon | `{ simulated: true }` — SPA shows "no real payment" notice (FR-028) |

## Pricing

| Method | Path | Auth | Purpose / FR |
|---|---|---|---|
| GET | `/region` | Anon | `{ countryCode, currencyCode, locale, shippingCountries[], states[] }` (FR-033) |
| GET | `/admin/tax-rates` | Admin | List by jurisdiction |
| PUT | `/admin/tax-rates/{jurisdictionCode}` | Admin | `{ rate, effectiveFrom }` (FR-030, SC-011) |
| GET/PUT | `/admin/store-settings` | Admin | Flat shipping fee (FR-024) |

## Ordering

| Method | Path | Auth | Purpose / FR |
|---|---|---|---|
| GET | `/cart` | Anon | Current cart (guest via `ashen_cart` cookie or customer); lines flag unavailable/price changed |
| PUT | `/cart/items/{variantId}` | Anon | `{ quantity }` (0 removes) (FR-018) |
| PUT/DELETE | `/cart/coupon` | Anon | `{ code }` → validated, replaces any existing (FR-038) |
| POST | `/checkout/quote` | Cust | `{ addressId }` → subtotal, discount, shipping, tax, total; stock/coupon problems listed (FR-019) |
| POST | `/checkout` | Cust | `{ addressId, savedCardId? \| card? , idempotencyKey }` → `201 { orderId, orderNumber }`; 409 with reason on stock/coupon/payment failure (FR-020/021) |
| GET | `/me/orders` · `/me/orders/{id}` | Cust | Own orders only; each includes `canCancel` (FR-035) |
| POST | `/me/orders/{id}/cancel` | Cust | Cancel own order within 1 hour while Paid; 409 if too late or already Shipped/Cancelled (FR-035) |
| GET | `/admin/orders?status=` · `/admin/orders/{id}` | Admin | (FR-036) |
| POST | `/admin/orders/{id}/ship` · `/deliver` · `/cancel` | Admin | Status transitions; 409 on invalid transition |

Guest cart merge is owned by Ordering: on any cart or checkout request from a signed-in customer
that still carries an `ashen_cart` cookie, Ordering merges that guest cart into the customer's cart
and deletes the cookie. Identity knows nothing about carts.
`idempotencyKey` is a UUID the SPA generates once per checkout attempt and reuses on retries of the
same request (double-click, network retry); it becomes the order's `CheckoutId`. A repeat with the
same key returns the original result. After a decline or stock/coupon conflict the SPA generates a
new key, so the next attempt is a fresh checkout.

## Promotions

| Method | Path | Auth | Purpose / FR |
|---|---|---|---|
| GET/POST | `/admin/coupons` | Admin | List/create (FR-037) |
| PUT | `/admin/coupons/{id}` | Admin | Edit |
| POST | `/admin/coupons/{id}/deactivate` | Admin | |

## Reviews

| Method | Path | Auth | Purpose / FR |
|---|---|---|---|
| GET | `/products/{productId}/reviews` | Anon | Visible reviews, newest first (FR-040) |
| PUT | `/products/{productId}/reviews/mine` | Cust | Create or update own review; 403 if not a verified buyer (FR-039) |
| DELETE | `/products/{productId}/reviews/mine` | Cust | |
| GET | `/admin/reviews?hidden=` | Admin | |
| POST | `/admin/reviews/{id}/hide` · `/unhide` | Admin | (FR-040) |

## Status codes

`200/201/204` success · `400` validation (problem details with `errors`) · `401` not signed in ·
`403` signed in but not allowed · `404` not found / not visible · `409` business conflict
(stock, coupon, payment declined, invalid status change, last admin) · `429` rate limited.
