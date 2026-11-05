# Research: Ashen Storefront Foundation

Each decision records what was chosen, why, and what was rejected. Versions are the current
stable lines as of 2026-11; exact patch versions are pinned in `Directory.Packages.props` and
`package.json` when scaffolding.

## R1. Front-end style: SPA vs server-rendered React

- **Decision**: React single-page app built with Vite, written in TypeScript.
- **Rationale**: The goal is to learn React itself, and a Vite SPA is React with nothing on top.
  .NET already provides the back end, so a Node server would be a second back end to run and
  deploy. Most of the store (account, cart, checkout, admin) is behind sign-in where SEO does
  not matter.
- **Alternatives considered**:
  - *Next.js / React Router framework mode (server rendering)*: best SEO for product pages and
    what many large stores use, but adds a Node server, server/client component rules, and a
    second place where business logic could leak. Rejected for phase 1.
  - *Blazor*: all-C#, but defeats the goal of learning React.
- **Known trade-off**: product pages are rendered in the browser, so search engines see less on
  first load. If SEO matters later, add pre-rendering of public catalog pages; the API does not
  change.

## R2. How the React app talks to .NET: Backend-for-Frontend (BFF)

- **Decision**: The ASP.NET Core host is the BFF. It serves the built SPA files and the JSON API
  from the **same origin** (`/api/*` for the API, everything else falls back to `index.html`).
  Authentication is an HTTP-only, Secure, `SameSite=Lax` cookie issued by the host. State-changing
  requests carry an anti-forgery token in a header. In development, the Vite dev server proxies
  `/api` to the .NET host so the browser still sees one origin.
- **Rationale**: Current guidance (OAuth for browser-based apps, IETF draft) recommends keeping
  tokens out of browser JavaScript. Same origin means no CORS configuration and cookies just work.
  One container to deploy.
- **Alternatives considered**: JWT in `localStorage` (exposed to XSS); separate SPA host + CORS
  (more moving parts); Duende BFF library (commercial licence, unnecessary because we are our own
  identity provider, not calling a separate one).

## R3. Identity and Google sign-in

- **Decision**: ASP.NET Core Identity with EF Core stores in the `identity` schema, cookie
  authentication, roles `Customer` and `Admin`, and the Google external login provider
  (`Microsoft.AspNetCore.Authentication.Google`). Google sign-in links to an existing account
  only when Google reports the email as verified. Lockout: 5 failures → 15 minutes (built-in
  options). Password reset via Identity's data-protection tokens (1-hour lifespan).
- **Breached passwords**: a custom `IPasswordValidator` checks a bundled list of the 100,000 most
  common breached passwords. No network call at sign-up.
- **Initial admin**: seeded at start-up from configuration (`Ashen:InitialAdmin:Email`); that user
  gets the Admin role on first sign-in or registration.
- **Alternatives considered**: Keycloak / Cognito / Auth0 (another system to run or pay for; good
  later if needed, and the cookie BFF pattern stays the same); Identity's bearer-token endpoints
  (`MapIdentityApi`) — tokens are not wanted in the browser.

## R4. Modular monolith layout

- **Decision**: 8 modules — **Catalog, Identity, Customers, Ordering, Payments, Pricing,
  Promotions, Reviews** — each as two C# projects:
  - `Ashen.<Module>`: everything `internal` (domain, features, data).
  - `Ashen.<Module>.Contracts`: the only public surface — query/command interfaces, DTOs,
    integration events.
  A module project may reference other modules' **Contracts** projects only; the compiler then
  enforces most boundaries, and architecture tests catch the rest.
- **Module placement of spec concepts**:
  - Featured products, categories, images, stock and stock reservations → Catalog
  - Addresses, wishlist, customer profile → Customers
  - Cart, checkout, orders → Ordering
  - Payments, saved cards, `IPaymentGateway` → Payments (card references are payment data)
  - Region settings, tax rates, flat shipping fee, `ITaxCalculator` → Pricing
  - Coupons and redemptions → Promotions
- **Alternatives considered**: one project per module with a public `Contracts` namespace
  (fewer projects, but only architecture tests enforce boundaries; the constitution asks for a
  contracts assembly); microservices (rejected by Principle IV).

## R5. Vertical slices without a mediator library

- **Decision**: Each use case is one file (or folder) holding the request/response records, the
  endpoint mapping, and a handler class registered in DI. Endpoints call the handler directly.
  Validation uses .NET 10's built-in minimal-API validation (`AddValidation()` with data
  annotations) for shape; business rules live in the domain.
- **Rationale**: MediatR moved to a commercial licence in 2025, and a mediator adds indirection
  that hides "what code runs when I call this endpoint" — bad for learning. Cross-cutting concerns
  (logging, errors, transactions) are handled by ASP.NET Core middleware and endpoint filters.
- **Alternatives considered**: MediatR (licence, indirection); FluentValidation (fine and free,
  but built-in validation covers phase 1); Wolverine/FastEndpoints (more framework to learn).

## R6. Data access and database

- **Decision**: One PostgreSQL database; one schema and one `DbContext` per module; EF Core 10
  with Npgsql; migrations per module, each with its own `__EFMigrationsHistory` table in its
  schema. `EFCore.NamingConventions` for snake_case table/column names. IDs are `Guid` v7
  (`Guid.CreateVersion7()`, time-ordered, index-friendly). Optimistic concurrency via PostgreSQL's
  `xmin` system column. Money mapped as an EF Core complex type (amount + currency columns).
  Text search uses `ILIKE` with a `pg_trgm` GIN index — ample for 1,000 products.
- **Rationale**: Schema-per-module keeps data ownership visible and makes later extraction a
  database copy, not a redesign. No repository layer over EF (Principle IV); slices use the
  module's `DbContext` directly.
- **Alternatives considered**: database-per-module (more to run locally, no gain yet); Dapper
  (user wants to learn EF Core); Elasticsearch (overkill for this catalog size).

## R7. Checkout across modules (stock, coupon, payment, order)

- **Decision**: Ordering orchestrates checkout synchronously through other modules' contracts,
  with **reservations and compensation** rather than one shared transaction:
  1. Load cart; get current variant prices and availability from Catalog.
  2. Validate coupon (Promotions) and calculate tax and shipping (Pricing).
  3. `Catalog.ReserveStock(checkoutId, lines)` — atomic conditional update; fails if insufficient.
  4. `Promotions.ReserveCoupon(code, customerId, checkoutId)` — atomic against usage limits.
  5. `Payments.Authorize(...)` via `IPaymentGateway`.
  6. Save the Order (status Paid) in Ordering, then confirm reservations and capture payment.
  On failure at any step, earlier steps are undone in reverse (release stock, release coupon,
  void authorization). Reservations expire after 15 minutes and a background job releases expired
  ones; likewise, payment authorizations with no order after 15 minutes are voided. Together
  these cover a crash mid-checkout.
- **Rationale**: Keeps each module's data private (Principle II) while guaranteeing no over-selling
  (FR-020, SC-007) and no charge without an order (FR-021). It is also exactly how the flow would
  work if modules became services.
- **Alternatives considered**: one transaction spanning several `DbContext`s on a shared
  connection (works in a monolith but couples modules' data and breaks on split); a full saga
  framework / message broker (Principle IV).

## R8. Module-to-module events

- **Decision**: A small in-process `IEventBus` in BuildingBlocks. Integration events (e.g.
  `OrderShipped`, `AccountDeletionRequested`) are published after the publishing module's
  `SaveChanges` succeeds; handlers in other modules run in-process.
- **Shortcut**: events are not persisted, so a crash between commit and handling loses the event.
  Acceptable for phase 1 (no money depends on events — checkout is synchronous, see R7). Upgrade
  to a transactional outbox when a module is split out or when an event becomes critical.
- **Queries between modules** (e.g. Reviews asking "has this customer received this product?")
  use Contracts interfaces synchronously — simpler than replicating data via events.

## R9. Payments: simulated gateway

- **Decision**: `IPaymentGateway` (in Payments) with operations Authorize, Capture, Void, Refund,
  and SaveCard (returns a token). Phase 1 implementation `SimulatedPaymentGateway`:
  - Luhn and expiry check; brand from number prefix.
  - Test cards (same numbers Stripe uses, so the knowledge transfers):
    `4242 4242 4242 4242` approve, `4000 0000 0000 0002` decline,
    `4000 0000 0000 9995` insufficient funds. Any other valid number approves.
  - Returns opaque tokens like `sim_card_<guid>`; only token, brand, last 4 and expiry are stored.
- **Front end**: card entry is one `CardEntry` component. With a real provider it would be
  replaced by the provider's hosted fields so card numbers never reach our server (keeping PCI
  scope minimal); with the simulator, the number is sent once to the API and discarded after
  tokenizing.
- **Alternatives considered**: Stripe test mode now (user chose simulation for phase 1).

## R10. Tax and regional settings

- **Decision**: Pricing module owns `RegionSetting` (country US, currency USD, locale `en-US`,
  shipping countries [US]), `TaxRate` rows (jurisdiction = US state code, rate, effective-from),
  and `StoreSettings` (flat shipping fee). `ITaxCalculator` has one implementation
  `TableTaxCalculator`: tax = round-half-up(merchandise after discount × state rate); shipping not
  taxed; missing state → 0. Seed: `IL` = 6.25%.
- **Front end** formats money and dates with the browser's `Intl.NumberFormat` /
  `Intl.DateTimeFormat`, using the locale and currency from `GET /api/region`.
- **Upgrade path**: an `ExternalTaxCalculator` (e.g. Avalara, Stripe Tax) replaces the table
  implementation for city/county rates; checkout code is unchanged.

## R11. Images, email, and other external capabilities

- **Images**: `IFileStorage` — `LocalFileStorage` (dev, files under a configured folder served at
  `/media`) and later `S3FileStorage`. Uploads checked for size (≤10 MB) and real format by file
  signature, not by extension.
- **Email**: `IEmailSender` — SMTP implementation; in development all mail goes to **Mailpit**
  (a local catch-all inbox with a web UI). Production later: Amazon SES (SMTP or API).
- **Clock**: .NET's built-in `TimeProvider` (no custom interface needed).

## R12. Local development orchestration

- **Decision**: **.NET Aspire** AppHost starts PostgreSQL, Mailpit, the API, and the Vite dev
  server with one command, and provides a dashboard for logs and traces. `Ashen.ServiceDefaults`
  wires OpenTelemetry, health checks and resilience.
- **Alternatives considered**: Docker Compose (universal and fine, but no dashboard and you start
  the API/Vite separately). Aspire adds two small projects and is mainstream for new .NET apps.

## R13. Front-end libraries

- **Decision** (minimum set, each justified):
  - React 19, React Router 8 (data mode, client-side) — routing.
  - TanStack Query 5 — server-state caching, loading/error states, cache invalidation after
    mutations.
  - `openapi-typescript` + `openapi-fetch` — TypeScript types generated from the API's built-in
    OpenAPI document, so front-end and back-end contracts cannot drift silently.
  - CSS Modules (built into Vite) — styling; visual design comes later.
  - Forms: plain React with controlled inputs and native validation first; add a form library
    only if checkout/admin forms prove painful.
- **Structure**: feature folders mirroring the back-end modules (`features/catalog`,
  `features/cart`, `features/checkout`, `features/account`, `features/admin`, …). Admin pages are
  in the same app under `/admin`, lazy-loaded; the server enforces the Admin role regardless.
- **Alternatives considered**: Redux (server state belongs in TanStack Query; little client state
  left); Tailwind / component libraries (design discussion deferred).

## R14. Testing stack

- **Back end**: xUnit v3 with built-in asserts; `WebApplicationFactory` + **Testcontainers**
  PostgreSQL for integration tests; plain reflection over assembly references and public types for boundary tests (no package needed).
- **Front end**: Vitest + React Testing Library; **Playwright** for the end-to-end smoke test
  (browse → cart → checkout).
- **Alternatives considered**: FluentAssertions (commercial licence from v8); EF in-memory provider
  (forbidden by Principle VII — it does not behave like PostgreSQL).

## R15. Production shape (target, not built in this feature)

- One container image (multi-stage Dockerfile: Node builds the SPA → `dotnet publish` with the SPA
  in `wwwroot`). On AWS: ECS Fargate behind an Application Load Balancer, Amazon RDS for
  PostgreSQL, S3 for images, SES for email, Secrets Manager for secrets. Infrastructure-as-code is
  a later feature.
- **This feature delivers**: the Dockerfile and a container that runs locally; no cloud resources.
