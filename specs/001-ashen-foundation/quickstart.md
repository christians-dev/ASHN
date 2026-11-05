# Quickstart & Validation: Ashen Storefront Foundation

How to run the store locally and prove each user story works. Endpoints are in
[contracts/http-api.md](contracts/http-api.md); entities and rules in [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK (version pinned in `global.json`)
- Node.js 22 LTS
- Docker Desktop (runs PostgreSQL and Mailpit containers for Aspire and Testcontainers)
- Aspire CLI (`dotnet tool install -g Aspire.Cli`) — optional; `dotnet run` on the AppHost also works
- Google OAuth client (optional, for Google sign-in): create a "Web application" client in Google
  Cloud Console with authorized redirect URI `http://localhost:5173/api/auth/signin-google` (the Vite address the browser uses in development), then
  store it with user secrets (never in the repo):

```bash
dotnet user-secrets --project src/Ashen.Api set "Authentication:Google:ClientId" "<id>"
```

```bash
dotnet user-secrets --project src/Ashen.Api set "Authentication:Google:ClientSecret" "<secret>"
```

## Run

```bash
npm ci --prefix web
```

```bash
aspire run
```

The Aspire dashboard opens and lists: `postgres`, `mailpit`, `api`, `web`. Open the `web` URL
(Vite, e.g. `http://localhost:5173`). Migrations and seed data (categories, ~20 sample products,
Illinois 6.25% tax rate, $5.00 flat shipping, `WELCOME10` coupon, initial admin email from
`Ashen:InitialAdmin:Email`) apply automatically in Development. Emails appear in the Mailpit UI.

## Test

```bash
dotnet test --solution Ashen.slnx
```

```bash
npm test --prefix web
```

```bash
npm run e2e --prefix web
```

`dotnet test` needs Docker running (Testcontainers). Architecture tests fail if a module
references another module's internals. It also runs the SC-005 performance check on a seeded
1,000-product catalog. `npm run e2e` drives the running store (start `aspire run` first; install the
browser once with `npx --prefix web playwright install chromium`) in desktop and 375 px touch projects.

## Validation scenarios

Run against seeded data. Test cards: [contracts/payment-gateway.md](contracts/payment-gateway.md).

| # | Story | Steps | Expected |
|---|---|---|---|
| 1 | US1 Browse | Open home → pick a category → filter size M → search "hoodie" → open a product | Featured products on home; filters narrow results; out-of-stock sizes disabled |
| 2 | US2 Accounts | Register `a@test.local` / strong password; sign out; 5 wrong passwords then a 6th | Signed in as Customer; 6th attempt refused for 15 min |
| 3 | US2 Reset | "Forgot password" → open link in Mailpit → set new password → reuse link | New password works; second use of link fails |
| 4 | US3 Admin catalog | Sign in as initial admin → create product with 3 variants + 2 images → publish | Visible to anonymous visitor; customer gets 403 on `/api/admin/products` |
| 5 | US4 Addresses | Add Illinois address, then a Canadian address | First saved as default; Canadian refused ("US only") |
| 6 | US5 + US6 Checkout | Guest adds 2 items → signs in (cart kept) → checkout to IL address with `4242…4242` | Tax = 6.25% of merchandise; order "Paid"; stock reduced; confirmation in Mailpit; "no real payment" notice shown |
| 7 | US5 Decline | Checkout with `4000 0000 0000 0002` | "Payment declined"; no order; stock and cart unchanged |
| 8 | US6 Tax | Same cart to a Texas address; admin sets IL to 7%; new quote | TX tax $0.00; IL now 7%; earlier order still shows 6.25% |
| 9 | US9 Coupon | Apply `WELCOME10` to a $50 cart; apply expired code | $5.00 off, tax computed after discount; expired code refused with reason |
| 10 | US7 Fulfilment | Admin ships then delivers order from #6; cancel a different Paid order | Customer sees Shipped → Delivered, shipping email sent; cancelled order restocks + refunded |
| 11 | US10 Reviews | Customer from #10 reviews the delivered product; another customer tries | Review shows, average updates; non-buyer gets 403; admin hide removes it from average |
| 12 | US11 Wishlist | Add 2 products, remove 1, move other to cart | Cart contains chosen variant |
| 13 | US8 Cards | Save `5555…4444`; view; delete | Only brand/last4/expiry shown; DB contains no full number |
| 14 | US12 Admins | Promote customer to admin; last admin tries to revoke self | Promotion works; self-revoke as last admin → 409 |
| 15 | SC-007 Race | Integration test: 100 parallel checkouts for a variant with stock 1 | Exactly 1 order, 99 × 409, 0 captured payments without order |
| 16 | US7 Customer cancel | Customer cancels a fresh Paid order; tries again on an order placed > 1 hour ago (adjust `PlacedAt` in the database or use the test clock) | First is Cancelled, restocked and refunded; second has no Cancel button and the API returns 409 |
| 17 | US3 Options | Admin creates a shoe with options Size, Colour, Width, adds variants, tries a duplicate combination | Product page shows three selectors; duplicate rejected (409); Size/Colour filters still find it |

## Container smoke check and load test

```bash
docker build -t ashen:local .
```

The image serves the built SPA and `/api` on port 8080 as a non-root user. It needs
`ConnectionStrings__ashen`, `Ashen__Store__BaseUrl` and `Email__Smtp__Host`; set
`Ashen__MigrateOnStartup=true` to create the schema and `Catalog__Seed__LargeCatalog=1000` for a
1,000-product catalog. Liveness: `GET /alive`. The SC-006 load test (200 shoppers, k6 in Docker) and
the exact commands are in the README under "Load test".
