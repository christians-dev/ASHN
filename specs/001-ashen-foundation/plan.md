# Implementation Plan: Ashen Storefront Foundation

**Branch**: `001-ashen-foundation` (spec folder; no git branch created yet) | **Date**: 2026-11-03 |
**Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-ashen-foundation/spec.md`

## Summary

Create the Ashen repository and the complete phase-1 store: catalog browsing with a featured home
page, email/password and Google sign-in, addresses, cart and checkout with simulated payments,
US sales tax (Illinois configured), coupons, reviews, wishlist, order fulfilment, and admin tools.

Technical approach (see [research.md](research.md)):

- **Front end**: a React 19 + TypeScript single-page app built with Vite.
- **Back end**: one ASP.NET Core (.NET 10) host. It is the app's Backend-for-Frontend: it serves
  the SPA and a JSON API from the same origin, and signs people in with secure cookies.
- **Module layout**: the back end is a modular monolith of 8 modules. Each module is organised
  into vertical slices, owns its own PostgreSQL schema through EF Core 10, and exposes a public
  Contracts project.
- **Checkout**: Ordering coordinates checkout across Catalog, Promotions, Pricing and Payments.
  Each step reserves what it needs (stock, coupon, payment), and if a later step fails, the
  earlier ones are undone.
- **Swappable parts**: payments, tax, email and file storage each sit behind an interface.
  Phase 1 uses a `SimulatedPaymentGateway`.
- **Local development**: .NET Aspire starts everything with one command.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (LTS); TypeScript 7 on Node.js 22 LTS (front-end build only)

**Primary Dependencies**:
- **Back end**: ASP.NET Core 10 (minimal APIs, Identity, Google auth, built-in OpenAPI and
  validation, rate limiting), EF Core 10 + Npgsql, EFCore.NamingConventions,
  Microsoft.AspNetCore.DataProtection.EntityFrameworkCore (sign-in cookie keys in PostgreSQL, so every ECS task and
  every redeploy reads the same keys; without it each container makes its own and sessions break).
- **Local dev and observability**: .NET Aspire (AppHost and ServiceDefaults), OpenTelemetry.
- **Front end**: React 19, React Router 8, TanStack Query 5, openapi-typescript and openapi-fetch.

**Storage**: PostgreSQL 17 (one database, schema per module, `pg_trgm` extension); product images
on the local file system behind `IFileStorage` (S3 later)

**Testing**:
- **Back end**: xUnit v3, `WebApplicationFactory`, Testcontainers (PostgreSQL); module boundary tests use plain reflection.
- **Front end**: Vitest + React Testing Library, and Playwright for end-to-end tests.

**Target Platform**: Linux container (ASP.NET Core). Runs locally under Aspire; production target
is AWS ECS Fargate + RDS PostgreSQL (not provisioned in this feature).

**Project Type**: Web application — SPA front end + modular-monolith back end in one monorepo

**Performance Goals**:
- Catalog pages show within 2 s for 95% of views with 1,000 products (SC-005).
- 200 shoppers can browse at the same time without slowdown (SC-006).

**Constraints**:
- No variant is ever over-sold, and no customer is charged without an order (SC-007, FR-020/021).
- No card data is stored (SC-009).
- WCAG 2.2 AA.
- Only same-origin cookie authentication; no tokens in the browser.

**Scale/Scope**: about 1,000 products, a few thousand customers, 12 user stories, 8 back-end
modules, about 35 SPA screens (shop and admin)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Gate | Pre-design | Post-design |
|---|-----------|------|-----------|-------------|
| I | Modular monolith of vertical slices | One deployable; 8 named modules; code grouped by feature, no global layer folders | ✅ | ✅ Project tree below; slices in `Features/` |
| II | Module autonomy | Schema + DbContext per module; cross-module access only via Contracts or events; IDs only; arch tests | ✅ | ✅ R4, R7, R8; [contracts/module-contracts.md](contracts/module-contracts.md); `Ashen.ArchitectureTests` |
| III | Ports for external capabilities | Payment, tax, email, file storage, external sign-in, clock behind abstractions; region from data | ✅ | ✅ `IPaymentGateway`, `ITaxCalculator`, `IEmailSender`, `IFileStorage`, `TimeProvider`, `RegionSetting` |
| IV | Simplicity | No repo/UoW layers, no mediator, no broker; each package justified | ✅ | ✅ R5, R13; package list in Technical Context, each justified in research |
| V | Money, tax & invariants | `Money` value object (decimal + currency); aggregates guard invariants; order snapshots; concurrency on stock/coupons | ✅ | ✅ [data-model.md](data-model.md); `xmin` concurrency; reservations (R7) |
| VI | Secure by default | BFF cookies + antiforgery; 2 roles; explicit authorization per endpoint; no card data; secrets out of repo; ProblemDetails | ✅ | ✅ R2, R3; [contracts/http-api.md](contracts/http-api.md) lists auth per endpoint |
| VII | Tested where it matters | Domain, auth, contracts, boundaries, DB slices on real Postgres, FE logic + E2E smoke | ✅ | ✅ Test projects below; quickstart scenarios |
| VIII | Reliable under concurrency & failure | Conditional updates/`xmin` on shared counters; idempotent checkout and payments; compensation + expiry clean-up; timeouts on external calls; `Result` for expected outcomes | ✅ | ✅ R7 reservations + `StaleAuthorizationVoider`; idempotency key = `CheckoutId`; SMTP timeout; concurrency tests |
| IX | Observable & configurable | Structured `ILogger` + OpenTelemetry; no secrets/card data/PII in logs; Options pattern with `ValidateOnStart` | ✅ | ✅ ServiceDefaults (R12); `StoreOptions` and module options validated at start-up; logging rules in host task |
| X | Tenant-ready, single-tenant | No tenancy built; store-wide settings in data/config; base URL and store name from config | ✅ | ✅ `RegionSetting`/`TaxRate`/`StoreSettings` in data; `StoreOptions` (name, base URL, sender) for emails and links |
| Stack | .NET 10, EF Core 10, PostgreSQL, React+Vite+TS, containers/ECS | ✅ | ✅ |

Also checked against the v1.1.0 additions to IV (framework conventions, proportionality), V (database
constraints back up domain rules) and VI (secure coding, security headers, dependency scanning):
covered by NuGet audit + `npm audit` in CI, security-header middleware, and check constraints in
each module's EF configuration.

**Result**: PASS against constitution v1.1.0. Deliberate simplifications made during implementation are listed,
with their upgrade triggers, under Complexity Tracking.

Notes worth flagging (not violations):

- **Aspire (R12)**: adds two projects. This is justified by the constitution's rule that
  everything runs locally with a single command.
- **In-process events without an outbox (R8)**: a documented shortcut. Nothing that involves
  money depends on these events.

## Project Structure

### Documentation (this feature)

```text
specs/001-ashen-foundation/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   ├── http-api.md          # REST endpoints, auth per endpoint
│   ├── module-contracts.md  # cross-module C# interfaces and events
│   └── payment-gateway.md   # IPaymentGateway + simulated test cards
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
Ashen.slnx
global.json                      # pins .NET 10 SDK
Directory.Build.props            # nullable, warnings-as-errors, analyzers
Directory.Packages.props         # central package versions
.editorconfig
Dockerfile                       # SPA build → dotnet publish → one image
README.md

src/
├── Ashen.AppHost/               # Aspire: Postgres, Mailpit, Api, Vite dev server
├── Ashen.ServiceDefaults/       # OpenTelemetry, health checks, resilience
├── Ashen.Api/                   # the host / BFF
│   ├── Program.cs               # registers modules, auth, antiforgery, ProblemDetails, SPA fallback
│   └── wwwroot/                 # built SPA (publish output only)
├── Ashen.BuildingBlocks/        # Money, Entity/AggregateRoot, Result, IEventBus, AuditEntry,
│                                # endpoint helpers, ModuleDbContext base
└── Modules/
    ├── Catalog/
    │   ├── Ashen.Catalog.Contracts/     # ICatalogModule, DTOs, events (public)
    │   └── Ashen.Catalog/               # all internal
    │       ├── CatalogModule.cs         # AddCatalog(services) / MapCatalog(endpoints)
    │       ├── Domain/                  # Product (root), Variant, ProductImage, Category, StockReservation
    │       ├── Features/
    │       │   ├── BrowseProducts.cs    # request + endpoint + handler in one file
    │       │   ├── GetProduct.cs
    │       │   ├── GetHomePage.cs
    │       │   └── Admin/CreateProduct.cs, PublishProduct.cs, AdjustStock.cs, ...
    │       └── Data/                    # CatalogDbContext, configurations, Migrations/
    ├── Identity/      (Ashen.Identity + .Contracts)    # accounts, roles, Google, lockout, admin grants
    ├── Customers/     (Ashen.Customers + .Contracts)   # profile, addresses, wishlist
    ├── Ordering/      (Ashen.Ordering + .Contracts)    # cart, checkout orchestration, orders
    ├── Payments/      (Ashen.Payments + .Contracts)    # IPaymentGateway, SimulatedPaymentGateway, saved cards
    ├── Pricing/       (Ashen.Pricing + .Contracts)     # region, tax rates, ITaxCalculator, shipping fee
    ├── Promotions/    (Ashen.Promotions + .Contracts)  # coupons, redemptions
    └── Reviews/       (Ashen.Reviews + .Contracts)     # reviews, moderation

tests/
├── Ashen.UnitTests/             # domain rules: Money, tax, coupons, cart, order state, simulated gateway
├── Ashen.IntegrationTests/      # API slices on real Postgres (Testcontainers), auth rules, checkout races
└── Ashen.ArchitectureTests/     # module boundaries, no layer folders, internal-by-default

web/                             # React SPA (Vite + TypeScript)
├── package.json
├── vite.config.ts               # dev proxy /api → Ashen.Api
├── src/
│   ├── main.tsx, App.tsx, routes.tsx
│   ├── api/                     # generated OpenAPI types + typed fetch client (antiforgery header)
│   ├── shared/                  # layout, money/date formatting (Intl), auth context, ui primitives
│   └── features/
│       ├── catalog/  home/  account/  addresses/  cart/  checkout/
│       ├── orders/  wishlist/  reviews/  payment-methods/
│       └── admin/               # products, categories, featured, orders, coupons, tax, reviews, admins
└── tests/
    ├── unit/                    # Vitest + Testing Library
    └── e2e/                     # Playwright smoke: browse → cart → checkout
```

**Structure Decision**:

- **Two top-level apps in one repo**: `src/` holds the .NET solution and `web/` holds the React
  app.
- **Back-end modules**: each lives under `src/Modules/<Name>/` as a pair of projects (internal
  implementation plus public Contracts). A module project may reference `Ashen.BuildingBlocks` and
  other modules' `.Contracts` projects only.
- **The host**: `Ashen.Api` is the only project that references every module. It composes them
  in `Program.cs`.
- **Tests**: three test projects, split by kind of test rather than by module. This keeps the
  solution small; per-module test projects can be added if it grows.

### Request flow (BFF)

```text
Browser ──same origin──► Ashen.Api
  GET /products/...      → static SPA (index.html fallback)
  GET/POST /api/...      → module endpoints (cookie auth + antiforgery on writes)
  GET /api/auth/google   → Google OAuth redirect → back to /api/auth/google/callback → cookie
Dev only: Browser → Vite :5173 ──proxy /api──► Ashen.Api
```

### Checkout sequence (R7)

```text
Ordering.PlaceOrder
  ├─ Catalog.GetVariantsForCheckout      (prices, availability)
  ├─ Promotions.ValidateCoupon           (discount)
  ├─ Pricing.Quote                       (tax by state, shipping fee)
  ├─ Catalog.ReserveStock     ──fail──►  stop: "only N available"
  ├─ Promotions.ReserveCoupon ──fail──►  release stock
  ├─ Payments.Authorize       ──fail──►  release coupon, release stock: "payment declined"
  ├─ save Order (Paid)        ──fail──►  void payment, release coupon, release stock
  └─ confirm stock + coupon, capture payment, publish OrderPlaced (→ confirmation email)
Background: release reservations older than 15 minutes.
```

## Phase 2 Approach (for /speckit-tasks)

Suggested ordering:

1. **Setup**: solution, Aspire, BuildingBlocks, host with SPA fallback, `web/` scaffold, CI
   build, architecture tests.
2. **Foundational**: Identity module and auth plumbing (needed by most stories), Pricing region
   endpoint, error handling, audit.
3. **User stories in priority order**: US1 → US12, each with its Principle VII tests.
4. **Polish**: Dockerfile, Playwright smoke test, accessibility pass, README.

## Complexity Tracking

Deliberate shortcuts taken during implementation (Principle IV: the simpler option until it hurts). Each is marked
`shortcut:` in the code; `grep -rn "shortcut:" src Dockerfile` lists them.

| Shortcut | Where | Why it is acceptable now | Upgrade when |
|---|---|---|---|
| Email via `System.Net.Mail.SmtpClient` | `BuildingBlocks/Email/Email.cs` | Mailpit locally; one SMTP host is enough | Production email: switch to SES or MailKit |
| Integration events in process, not persisted | `BuildingBlocks/Events/InProcessEventBus.cs` | One process, handlers run in the same request (R8) | A module is extracted, or a lost event costs money: add an outbox |
| Rating sort ranks matches in memory | `Catalog/Features/BrowseProducts.cs` | Catalog of hundreds; SC-005 passes at 1,000 | Thousands of products: keep ratings in a Catalog column |
| Data Protection keys stored unencrypted | `Identity/IdentityModule.cs` | Database access is already privileged | Production: `ProtectKeysWith…` (e.g. AWS KMS) |
| Short breached-password list | `Identity/Passwords/common-passwords.txt` | Blocks the most common passwords without a large file | Stronger policy needed: Have I Been Pwned range API |
| My orders not paged | `Ordering/Features/MyOrders.cs` | A customer has few orders | Customers with many orders: page like the admin list |
| Restock may repeat after a crash mid-settlement | `Ordering/Shared/OrderCancellation.cs` | Worst case is too much stock, never wrong money; refunds are idempotent | Stock accuracy matters: record restocks per order in Catalog |
| Gateway idempotency answers in memory | `Payments/Gateway/SimulatedPaymentGateway.cs` | Simulated gateway only | Replaced by a real provider, which keeps them |
| Product reviews not paged | `Reviews/Features/Reviews.cs` | Products have few reviews | Products gather hundreds of reviews |
| Product images on container disk | `Dockerfile` | One task; images are re-uploadable | More than one task in production: S3 behind `IFileStorage` |
