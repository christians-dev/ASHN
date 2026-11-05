---

description: "Task list for the Ashen storefront foundation"
---

# Tasks: Ashen Storefront Foundation

**Input**: Design documents from `specs/001-ashen-foundation/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Per constitution Principle VII, test tasks are included for domain rules (money, tax,
coupons, stock, order status), authorization, module contracts, module boundaries, and
DB-touching slices (integration tests against real PostgreSQL via Testcontainers). Trivial CRUD
mapping has no dedicated tests. Write each story's tests first and confirm they fail.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested
on its own after the Foundational phase.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: User story from spec.md (US1–US12)

## Path Conventions

- Back end: `src/` (host `src/Ashen.Api/`, shared `src/Ashen.BuildingBlocks/`)
- Module `X`: implementation `src/Modules/X/Ashen.X/` (all `internal`), public surface
  `src/Modules/X/Ashen.X.Contracts/`
- Inside a module: `Domain/`, `Features/` (one file per slice: request/response records +
  endpoint mapping + handler), `Data/` (DbContext, configurations, `Migrations/`), `XModule.cs`
  (DI registration + endpoint mapping), `XModuleApi.cs` (implements the Contracts interface)
- Back-end tests: `tests/Ashen.UnitTests/`, `tests/Ashen.IntegrationTests/`,
  `tests/Ashen.ArchitectureTests/`
- Front end: `web/src/` (feature folders under `web/src/features/`), tests in `web/tests/`
- Each module's EF configuration adds check constraints for numeric invariants (e.g. quantities and stock ≥ 0, rating 1–5, rate 0–1) alongside unique keys (constitution V)
- Logging follows constitution IX: `ILogger` message templates, customers identified by id only; never log emails, names, addresses, passwords, tokens or card data. Log business events at Information with ids only: order placed, cancelled (by customer or admin), shipped, delivered; payment approved, declined, voided, refunded; coupon redeemed; and every admin change (alongside its audit entry). Log failures and compensations at Warning or Error with enough context (ids, step, reason) to act on
- Every admin slice writes an audit entry via `IAuditLog` (FR-047) and requires the `Admin`
  policy; every endpoint declares its authorization explicitly (constitution VI)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repository, solution, projects, and tooling exist and build.

- [X] T001 Create root files: `global.json` (pin .NET 10 SDK), `Directory.Build.props` (net10.0, nullable enable, implicit usings, `TreatWarningsAsErrors`, .NET analyzers), `Directory.Packages.props` (central package management; NuGet audit on with `NuGetAuditMode=all` and `NuGetAuditLevel=high`, so known high/critical vulnerabilities fail the build via warnings-as-errors), `.editorconfig`, `.gitignore` (dotnet + node), and empty `Ashen.slnx` at repo root
- [X] T002 Create `src/Ashen.ServiceDefaults/` from the Aspire ServiceDefaults template (OpenTelemetry, health checks, service discovery, resilience) and add to `Ashen.slnx`
- [X] T003 Create `src/Ashen.Api/` (ASP.NET Core empty web project) referencing ServiceDefaults with a minimal `Program.cs` that runs, and add to `Ashen.slnx`
- [X] T004 [P] Create `src/Ashen.BuildingBlocks/` class library and add to `Ashen.slnx`
- [X] T005 Create the 16 module projects under `src/Modules/` (Catalog, Identity, Customers, Ordering, Payments, Pricing, Promotions, Reviews — each `Ashen.X` + `Ashen.X.Contracts`), each `Ashen.X` referencing `Ashen.BuildingBlocks` and its own Contracts, add `InternalsVisibleTo` for `Ashen.UnitTests`/`Ashen.IntegrationTests`, reference all from `src/Ashen.Api/Ashen.Api.csproj`, and add all to `Ashen.slnx`
- [X] T006 [P] Create test projects `tests/Ashen.UnitTests/`, `tests/Ashen.IntegrationTests/`, `tests/Ashen.ArchitectureTests/` (xUnit v3; integration adds `Microsoft.AspNetCore.Mvc.Testing` + `Testcontainers.PostgreSql`; architecture tests use reflection, no extra package) and add to `Ashen.slnx`
- [X] T007 [P] Scaffold `web/` with Vite `react-ts` template; add `react-router`, `@tanstack/react-query`, `openapi-fetch`, dev deps `openapi-typescript`, `vitest`, `@testing-library/react`, `@testing-library/user-event`, `jsdom`, `@playwright/test`, `@axe-core/playwright`, oxlint (template default; `react/no-danger` as an error, constitution VI) + Prettier configs; run `openapi-typescript` via a pinned `npx` (it does not support TypeScript 7 as a peer); npm scripts `dev`, `build`, `lint`, `test`, `e2e`, `api:types` in `web/package.json`
- [X] T008 [P] Configure `web/vite.config.ts`: dev proxy for `/api` and `/media` to the API URL from the Aspire-provided environment variable, Vitest `jsdom` environment, build output to `web/dist`
- [X] T009 Create `src/Ashen.AppHost/AppHost.cs` (Aspire): PostgreSQL resource with database `ashen` and pgAdmin optional, Mailpit container (SMTP + UI), `Ashen.Api` project with references/wait-for, and the Vite app from `web/` via `AddViteApp`; add to `Ashen.slnx`
- [X] T010 [P] Add CI workflow `.github/workflows/ci.yml`: `dotnet format --verify-no-changes`, `dotnet build`, `dotnet test` (Docker available), `npm ci`, `npm audit --audit-level=high`, `npm run lint`, `npm test`, `npm run build` in `web/`
- [X] T011 [P] Write `README.md` at repo root: what Ashen is, prerequisites, run/test commands, link to `specs/001-ashen-foundation/quickstart.md` and the constitution

**Checkpoint**: `dotnet build` and `npm run build --prefix web` succeed; `aspire run` starts Postgres, Mailpit, API, and Vite.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared building blocks, host pipeline, identity plumbing, region data, test harness,
and SPA shell that every story needs.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Building blocks

- [X] T012 [P] Implement `Money` readonly record struct (decimal amount, ISO currency; add/subtract/multiply; `RoundToCents` half-up via `MidpointRounding.AwayFromZero`; throws on currency mismatch; `Zero(currency)`) in `src/Ashen.BuildingBlocks/Money.cs`
- [X] T013 [P] Unit tests for `Money` (arithmetic, rounding 0.005 → 0.01, currency mismatch, negative guard where relevant) in `tests/Ashen.UnitTests/BuildingBlocks/MoneyTests.cs`
- [X] T014 [P] Implement `Entity`/`AggregateRoot` base (Guid v7 id via `Guid.CreateVersion7()`, pending integration events list) in `src/Ashen.BuildingBlocks/Domain/AggregateRoot.cs` and `Result`/`Error` types (error kinds: Validation, NotFound, Conflict, Forbidden) in `src/Ashen.BuildingBlocks/Result.cs`
- [X] T015 [P] Implement `IIntegrationEvent`, `IIntegrationEventHandler<T>`, `IEventBus` and `InProcessEventBus` (resolves handlers from DI, runs after commit, logs handler failures) in `src/Ashen.BuildingBlocks/Events/` — add `// shortcut: events not persisted, upgrade to outbox when a module is extracted (research R8)`
- [X] T016 [P] Implement `AuditEntry` entity + EF configuration and `IAuditLog.Record(action, entityType, entityId, details)` (uses current user id) in `src/Ashen.BuildingBlocks/Audit/`
- [X] T017 Implement module data conventions as composable helpers in `src/Ashen.BuildingBlocks/Data/` so any DbContext can use them, including ones that must inherit a framework base class (Identity, T022): `ModelBuilder.ApplyModuleConventions(schema)` (default schema, `Money` complex-type convention `*_amount numeric(12,2)` + `*_currency char(3)`, `audit_entries` table, `xmin` concurrency helper) in `ModelBuilderExtensions.cs`; `DbContextOptionsBuilder.UseModuleDefaults(connectionString, schema)` (Npgsql, snake_case via `EFCore.NamingConventions`, migrations history table in the module schema) in `DbContextOptionsBuilderExtensions.cs`; `IntegrationEventDispatcher : SaveChangesInterceptor` publishing aggregates' pending events through `IEventBus` after a successful save in `IntegrationEventDispatcher.cs`; and a thin `ModuleDbContext` base class that just calls these, for ordinary modules, in `ModuleDbContext.cs`
- [X] T018 [P] Implement web helpers in `src/Ashen.BuildingBlocks/Web/`: `IModule` (`AddServices(IServiceCollection, IConfiguration)`, `MapEndpoints(IEndpointRouteBuilder)`), `Result` → `IResult` mapping to RFC 9457 problem details (400/403/404/409), `PagedRequest`/`PagedResult<T>` records
- [X] T019 [P] Implement `IEmailSender` + `SmtpEmailSender` (options `Email:Smtp:*` validated at start-up, 10-second send timeout, sender name/address from `StoreOptions` in T021, so do T021 first or together) and simple text/HTML template helper in `src/Ashen.BuildingBlocks/Email/` — add `// shortcut: System.Net.Mail SmtpClient, switch to SES/MailKit for production`

### Host pipeline (BFF)

- [X] T020 Compose the host in `src/Ashen.Api/Program.cs`: ServiceDefaults, register each `IModule`, ProblemDetails + exception handler (no internal details), built-in validation (`AddValidation()`), OpenAPI document at `/openapi/v1.json` in Development, antiforgery with header `X-XSRF-TOKEN` validated on all non-GET `/api` endpoints, authorization with `FallbackPolicy = RequireAuthenticatedUser` and named policy `Admin`, rate limiter policy `auth` (10 requests/min per IP), `MapFallbackToFile("index.html")` for non-`/api` routes, security headers middleware (HSTS outside Development, `Content-Security-Policy` with `default-src 'self'; img-src 'self' data:; frame-ancestors 'none'` outside Development so Vite hot reload keeps working, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`), HTTP request logging without bodies, cookies or query strings, and `public partial class Program` for tests
- [X] T021 Add `StoreOptions` (StoreName, BaseUrl, SupportEmail, EmailSenderName) bound from `Ashen:Store` with data-annotation validation and `ValidateOnStart`, and use the same `AddOptions<T>().BindConfiguration(...).ValidateDataAnnotations().ValidateOnStart()` pattern for every module's options, in `src/Ashen.BuildingBlocks/StoreOptions.cs` and `src/Ashen.Api/Program.cs` (constitution IX, X)
- [X] T022 Create Identity data: `Account : IdentityUser<Guid>` (DisplayName, Status, CreatedAt), `AshenIdentityDbContext : IdentityDbContext<Account, IdentityRole<Guid>, Guid>` in schema `identity` (cannot inherit `ModuleDbContext`, so it calls `ApplyModuleConventions("identity")` in `OnModelCreating` and is registered with `UseModuleDefaults` + the `IntegrationEventDispatcher` interceptor from T017), role seed `Customer`/`Admin`, initial migration in `src/Modules/Identity/Ashen.Identity/Data/`
- [X] T023 Implement `IdentityModule` in `src/Modules/Identity/Ashen.Identity/IdentityModule.cs`: `AddIdentityCore<Account>` + roles + EF stores, password options (min length 8), lockout (5 attempts, 15 minutes, enabled for new users), token lifespan 1 hour, cookie authentication (HttpOnly, `SecurePolicy.Always`, `SameSite=Lax`, sliding 14-day expiry, return 401/403 for `/api` instead of redirects)
- [X] T024 [P] Implement `ICurrentUser` in `src/Modules/Identity/Ashen.Identity.Contracts/ICurrentUser.cs` and `HttpCurrentUser` (from `HttpContext.User`) in `src/Modules/Identity/Ashen.Identity/HttpCurrentUser.cs`
- [X] T025 Implement `InitialAdminSeeder` in `src/Modules/Identity/Ashen.Identity/InitialAdminSeeder.cs`: ensures the account whose email equals `Ashen:InitialAdmin:Email` has the Admin role (on start-up if it exists, and on registration/first Google sign-in) (FR-014)
- [X] T026 Implement `GET /api/auth/session` (anonymous; returns `{isAuthenticated, userId, email, displayName, roles}` and issues the `XSRF-TOKEN` cookie) in `src/Modules/Identity/Ashen.Identity/Features/GetSession.cs`

### Pricing region (needed for formatting and address rules)

- [X] T027 Create Pricing domain + data: `RegionSetting`, `TaxRate`, `StoreSettings` per data-model.md, `PricingDbContext` (schema `pricing`), migration, and seed (US, USD, en-US, shipping countries [US], `IL` = 0.0625 effective 2026-01-01, flat shipping $5.00) in `src/Modules/Pricing/Ashen.Pricing/`
- [X] T028 Implement `IPricingModule.GetActiveRegion` (incl. US state list with codes and names) in `src/Modules/Pricing/Ashen.Pricing/PricingModuleApi.cs` and anonymous `GET /api/region` in `src/Modules/Pricing/Ashen.Pricing/Features/GetRegion.cs`

### Database start-up

- [X] T029 Implement `DatabaseInitializer` in `src/Ashen.Api/DatabaseInitializer.cs`: when `Ashen:MigrateOnStartup` is true (default in Development), apply migrations for every module DbContext, then run module seeders (`pg_trgm` is created by the Catalog migration via `HasPostgresExtension`)

### Test harness

- [X] T030 Implement `AshenApiFactory` (WebApplicationFactory<Program> + shared `PostgreSqlContainer`, a fresh database per test class created on the shared container (`ashen_test_<guid>`, migrations applied, dropped afterwards; no extra package), Mailpit replaced by an in-memory `IEmailSender` capturing messages) and `ApiClient` helpers (register/sign in as customer or admin, carry cookies, fetch antiforgery token and send `X-XSRF-TOKEN`) in `tests/Ashen.IntegrationTests/Infrastructure/`
- [X] T031 [P] Architecture tests in `tests/Ashen.ArchitectureTests/ModuleBoundaryTests.cs`: each `Ashen.X` depends only on BuildingBlocks, its own Contracts and other modules' Contracts; no public types in `Ashen.X` except the `IModule` entry class and EF Core-generated migration classes (exclude namespaces ending in `.Migrations`, which EF tooling generates as `public`); Contracts depend on nothing but BuildingBlocks; no namespaces/folders named `Controllers`, `Services`, or `Repositories`
- [X] T032 [P] Integration tests for security defaults in `tests/Ashen.IntegrationTests/Platform/SecurityDefaultsTests.cs`: POST without antiforgery header → 400 and with it passes; (the anonymous → 401 and customer → 403 checks are asserted by the first admin endpoint's tests in US3, T072, since no admin route exists in this phase); unknown `/api` route → 404 problem details; non-API route returns `index.html`; security headers present on responses; host fails to start when a required option (e.g. `Ashen:Store:BaseUrl`) is missing

### SPA shell

- [X] T033 Add `api:types` script generating `web/src/api/schema.d.ts` from `/openapi/v1.json` and a typed client in `web/src/api/client.ts` (`openapi-fetch`, `credentials: 'same-origin'`, reads `XSRF-TOKEN` cookie into `X-XSRF-TOKEN` header on writes, throws typed problem-details errors)
- [X] T034 [P] App shell: `web/src/main.tsx`, `web/src/App.tsx` (QueryClientProvider), `web/src/routes.tsx` (React Router data router; public layout, `/admin/*` lazy route, not-found page), `web/src/shared/layout/SiteLayout.tsx` (header, nav, footer, skip link)
- [X] T035 [P] Session: `useSession` query + `RequireAuth` and `RequireAdmin` route guards in `web/src/shared/auth/`
- [X] T036 [P] Region + formatting: `useRegion` query and `formatMoney`/`formatDate` (`Intl.NumberFormat`/`Intl.DateTimeFormat` with region locale/currency) in `web/src/shared/format/`, with unit tests in `web/tests/unit/format.test.ts`
- [X] T037 [P] Accessible UI primitives (Button, TextField with label + error, Select, Alert, Spinner, Pagination, EmptyState) using CSS Modules in `web/src/shared/ui/`
- [X] T038 [P] Admin shell `web/src/features/admin/AdminLayout.tsx` with nav placeholders (Products, Categories, Featured, Orders, Coupons, Tax & Shipping, Reviews, Admins)

**Checkpoint**: API serves the SPA shell, `/api/auth/session` and `/api/region` work, all foundational tests pass.

---

## Phase 3: User Story 1 - Browse the catalog (Priority: P1) 🎯 MVP

**Goal**: Anyone can browse the home page, categories, search, filter, and view product details with variant availability.

**Independent Test**: With the seeded catalog, find a product by category, by search, and by filter; see out-of-stock sizes disabled; unpublished products return "not available" (quickstart #1).

### Tests for User Story 1 ⚠️

- [X] T039 [P] [US1] Unit tests for `Product` (publish requires ≥1 image and ≥1 variant; status transitions; variant `Available` and `IsPurchasable`; options: 1–3 unique names, variant must have exactly one allowed value per option, duplicate combination rejected, adding/removing options blocked once variants exist, removing a used value blocked) in `tests/Ashen.UnitTests/Catalog/ProductTests.cs`
- [X] T040 [P] [US1] Integration tests for browsing (list by category, filter by size/colour (standard option names, case-insensitive)/price, product with a non-standard option (e.g. Width) still listed and its detail returns all options, sort newest/price, search "hoodie", unpublished excluded and its detail → 404, home returns featured in order) in `tests/Ashen.IntegrationTests/Catalog/BrowseProductsTests.cs`

### Implementation for User Story 1

- [X] T041 [P] [US1] `Category` entity in `src/Modules/Catalog/Ashen.Catalog/Domain/Category.cs`
- [X] T042 [P] [US1] `Product` aggregate root with `Variant`, `ProductImage`, `ProductStatus` (Draft, Published, Unpublished, Archived), `IsFeatured`/`FeaturedPosition`, `HasBeenOrdered`, `ProductOption` definitions (1–3 named options with allowed values) and methods `DefineOptions/AddOptionValue/Publish/Unpublish/Archive/AddVariant/UpdateVariant/AddImage` enforcing the option rules in data-model.md in `src/Modules/Catalog/Ashen.Catalog/Domain/`
- [X] T043 [US1] `CatalogDbContext` (schema `catalog`) with configurations (product `options` jsonb, variant `option_values` jsonb, unique SKU/slug, unique `(product_id, option_key)`, `xmin` concurrency on Product and Variant, check constraints `stock_on_hand >= 0` and `stock_reserved >= 0`, trigram GIN index on `(name || ' ' || description)`, added by hand in the migration) and initial migration in `src/Modules/Catalog/Ashen.Catalog/Data/`
- [X] T044 [P] [US1] `IFileStorage` + `LocalFileStorage` (root from `Catalog:Media:RootPath`, public URL `/media/...`) in `src/Modules/Catalog/Ashen.Catalog/Images/`, and `/media` static file mapping in `src/Ashen.Api/Program.cs`
- [X] T045 [US1] Development seed: 5 categories, ~20 published products (each defining Size and Colour options, 2–4 variants each, sizes S–XL, 2 colours, plus one shoe product with a third `Width` option, some out of stock, 4 featured, 1 unpublished) with placeholder images in `src/Modules/Catalog/Ashen.Catalog/Data/CatalogSeeder.cs` (placeholder images are SVG tiles generated in code; enabled by `Catalog:Seed:SampleData`)
- [X] T046 [P] [US1] `GET /api/categories` slice in `src/Modules/Catalog/Ashen.Catalog/Features/ListCategories.cs`
- [X] T047 [US1] `GET /api/home` slice (featured published products by position + categories) in `src/Modules/Catalog/Ashen.Catalog/Features/GetHomePage.cs`
- [X] T048 [US1] `GET /api/products` slice (category, q, size and colour matched against the standard "Size"/"Colour" option names, minPrice, maxPrice, sort, paging; from-price; primary image) in `src/Modules/Catalog/Ashen.Catalog/Features/BrowseProducts.cs`
- [X] T049 [US1] `GET /api/products/{slug}` slice (images, option definitions, variants with `optionValues` and `isAvailable`, 404 unless Published) in `src/Modules/Catalog/Ashen.Catalog/Features/GetProduct.cs`
- [X] T050 [US1] `CatalogModule` registration/mapping (anonymous endpoints opt-in) and `ICatalogModule.GetProductSummaries` in `src/Modules/Catalog/Ashen.Catalog/CatalogModule.cs` and `src/Modules/Catalog/Ashen.Catalog/CatalogModuleApi.cs`, contract types in `src/Modules/Catalog/Ashen.Catalog.Contracts/`
- [X] T051 [P] [US1] `ProductCard` and `ProductGrid` components in `web/src/features/catalog/components/`
- [X] T052 [P] [US1] Home page (featured products + category links) in `web/src/features/home/HomePage.tsx`
- [X] T053 [US1] Product list page with filters, sort, pagination synced to URL search params in `web/src/features/catalog/ProductListPage.tsx` (routes `/c/:categorySlug` and `/search`)
- [X] T054 [US1] Header search box navigating to `/search?q=` in `web/src/shared/layout/SearchBox.tsx`
- [X] T055 [US1] Product detail page (gallery, `VariantSelector` showing one selector per product option (1–3) with combinations that are unavailable or don't exist disabled, "not available" state on 404) in `web/src/features/catalog/ProductDetailPage.tsx` and `web/src/features/catalog/components/VariantSelector.tsx`
- [X] T056 [P] [US1] Component test: `VariantSelector` renders 1, 2 and 3 options, disables unavailable/non-existent combinations, and reports the chosen variant in `web/tests/unit/VariantSelector.test.tsx`

**Checkpoint**: Visitors can browse the seeded store end to end.

---

## Phase 4: User Story 2 - Create an account and sign in (Priority: P2)

**Goal**: Email/password and Google sign-in, sign-out, password reset, lockout, profile edit, account deletion.

**Independent Test**: Register, sign out, sign in; Google sign-in creates/links an account; 6th failed attempt locked; reset link works once (quickstart #2–3).

### Tests for User Story 2 ⚠️

- [X] T057 [P] [US2] Unit tests for `BreachedPasswordValidator` (rejects listed passwords case-insensitively, accepts others) in `tests/Ashen.UnitTests/Identity/BreachedPasswordValidatorTests.cs`
- [X] T058 [P] [US2] Integration tests in `tests/Ashen.IntegrationTests/Identity/AccountTests.cs`: register → Customer role + signed in; duplicate email → 400; login; 5 failures then 6th refused; forgot password always 202; reset token works once; initial-admin email gets Admin role; profile update; delete account anonymizes and blocks sign-in
- [X] T059 [P] [US2] Integration tests for external sign-in in `tests/Ashen.IntegrationTests/Identity/GoogleSignInTests.cs` using a fake external authentication scheme: first sign-in creates account; verified email links to existing account; unverified email does not link; non-local `returnUrl` rejected
- [X] T060 [P] [US2] Component tests for `SignInPage` and `RegisterPage` (required-field and password-length messages, server problem-details errors shown per field, lockout message, "Continue with Google" links to `/api/auth/google?returnUrl=`) in `web/tests/unit/AuthForms.test.tsx`

### Implementation for User Story 2

- [X] T061 [P] [US2] `BreachedPasswordValidator : IPasswordValidator<Account>` with a bundled list as embedded resource (a short starter list; the owner chose not to bundle a full breached-password list) in `src/Modules/Identity/Ashen.Identity/Passwords/`
- [X] T062 [US2] `AccountDeletionRequested(UserId)` integration event in `src/Modules/Identity/Ashen.Identity.Contracts/Events.cs`
- [X] T063 [US2] `POST /api/auth/register` (Customer role, initial-admin check, sign-in) in `src/Modules/Identity/Ashen.Identity/Features/Register.cs`
- [X] T064 [US2] `POST /api/auth/login` (lockout-aware, `auth` rate limit) and `POST /api/auth/logout` in `src/Modules/Identity/Ashen.Identity/Features/Login.cs` and `Logout.cs`
- [X] T065 [US2] Google sign-in: `AddGoogle` from `Authentication:Google:*` config (skipped with a log warning when not configured), `GET /api/auth/google` challenge and `GET /api/auth/google/callback` (link only when `email_verified`, create otherwise, local `returnUrl` only) in `src/Modules/Identity/Ashen.Identity/Features/GoogleSignIn.cs`
- [X] T066 [US2] `POST /api/auth/password/forgot` (always 202, emails reset link built from `StoreOptions.BaseUrl`) and `POST /api/auth/password/reset` in `src/Modules/Identity/Ashen.Identity/Features/PasswordReset.cs`
- [X] T067 [US2] `GET/PUT /api/account` and `DELETE /api/account` (anonymize email/name, remove logins/password, Status=Deleted, sign out, publish `AccountDeletionRequested`) in `src/Modules/Identity/Ashen.Identity/Features/Account.cs`
- [X] T068 [P] [US2] Register and sign-in pages with "Continue with Google" in `web/src/features/account/RegisterPage.tsx` and `web/src/features/account/SignInPage.tsx`
- [X] T069 [P] [US2] Forgot and reset password pages in `web/src/features/account/PasswordResetPages.tsx`
- [X] T070 [US2] Header account menu (sign in/out, Admin link for admins) in `web/src/shared/layout/AccountMenu.tsx` and account page (edit name/email, delete account with confirmation) in `web/src/features/account/AccountPage.tsx`

**Checkpoint**: Shoppers and the initial admin can sign in; protected routes redirect to sign-in.

---

## Phase 5: User Story 3 - Admin manages the catalog (Priority: P3)

**Goal**: Admins create, edit, publish, unpublish, archive products; manage variants, stock, images, categories, featured products.

**Independent Test**: As admin, create a product with 3 variants and 2 images, publish it, see it as a visitor; customer gets 403 on admin APIs (quickstart #4). Depends on US1 (catalog) and US2 (admin sign-in).

### Tests for User Story 3 ⚠️

- [X] T071 [P] [US3] Unit tests for `ImageValidator` (JPEG/PNG/WebP signatures accepted, renamed text file rejected, >10 MB rejected) in `tests/Ashen.UnitTests/Catalog/ImageValidatorTests.cs`
- [X] T072 [P] [US3] Integration tests in `tests/Ashen.IntegrationTests/Catalog/AdminCatalogTests.cs`: create draft not visible; publish visible; customer → 403 on every admin catalog route; stock 0 → unavailable; delete with `HasBeenOrdered` → archived; delete non-empty category → 409; variant with values not matching the product's options → 400; duplicate option combination → 409; adding an option once variants exist → 409; audit entry written for create/publish/stock change

### Implementation for User Story 3

- [X] T073 [P] [US3] `ImageValidator` (file signature + size) in `src/Modules/Catalog/Ashen.Catalog/Images/ImageValidator.cs`
- [X] T074 [US3] Admin product slices `GET /api/admin/products`, `POST /api/admin/products`, `PUT /api/admin/products/{id}`, `POST .../publish|unpublish|archive`, `DELETE /api/admin/products/{id}` (archive when `HasBeenOrdered`) in `src/Modules/Catalog/Ashen.Catalog/Features/Admin/ManageProducts.cs`
- [X] T075 [US3] Options and variant slices `PUT /api/admin/products/{id}/options`, `POST /api/admin/products/{id}/variants`, `PUT .../variants/{variantId}`, `PUT .../variants/{variantId}/stock` in `src/Modules/Catalog/Ashen.Catalog/Features/Admin/ManageVariants.cs`
- [X] T076 [US3] Image slices upload (multipart), reorder, delete in `src/Modules/Catalog/Ashen.Catalog/Features/Admin/ManageImages.cs`
- [X] T077 [P] [US3] Category slices create/rename/delete-if-empty in `src/Modules/Catalog/Ashen.Catalog/Features/Admin/ManageCategories.cs`
- [X] T078 [P] [US3] `PUT /api/admin/featured` slice in `src/Modules/Catalog/Ashen.Catalog/Features/Admin/SetFeaturedProducts.cs`
- [X] T079 [P] [US3] Admin product list page (status filter) in `web/src/features/admin/products/ProductListAdminPage.tsx`
- [X] T080 [US3] Admin product editor (details form, options editor for up to 3 option names and their values, variants table with one column per option plus price/stock edit, image upload/reorder/delete, publish/unpublish/archive buttons) in `web/src/features/admin/products/ProductEditorPage.tsx`
- [X] T081 [P] [US3] Admin categories page in `web/src/features/admin/categories/CategoriesPage.tsx`
- [X] T082 [P] [US3] Admin featured products page (pick and order) in `web/src/features/admin/featured/FeaturedProductsPage.tsx`

**Checkpoint**: Admins run the catalog without database access.

---

## Phase 6: User Story 4 - Manage delivery addresses (Priority: P4)

**Goal**: Customers manage US delivery addresses with one default.

**Independent Test**: Add two addresses, set second default, delete first; Canadian address refused (quickstart #5).

### Tests for User Story 4 ⚠️

- [X] T083 [P] [US4] Unit tests for `CustomerProfile` address rules (first is default, exactly one default, deleting default promotes another) and `AddressValidator` (state code, ZIP `^\d{5}(-\d{4})?$`, country in shipping countries) in `tests/Ashen.UnitTests/Customers/AddressTests.cs`
- [X] T084 [P] [US4] Integration tests for `/api/me/addresses` (CRUD, default, validation 400, other customer's address → 404) in `tests/Ashen.IntegrationTests/Customers/AddressApiTests.cs`
- [X] T085 [P] [US4] Component test for `AddressForm` (required fields flagged, ZIP format checked, state dropdown filled from region, server field errors mapped to inputs, submit disabled while saving) in `web/tests/unit/AddressForm.test.tsx`

### Implementation for User Story 4

- [X] T086 [US4] `CustomerProfile` aggregate with `Address` and `WishlistItem` children in `src/Modules/Customers/Ashen.Customers/Domain/`
- [X] T087 [US4] `CustomersDbContext` (schema `customers`) + configurations + migration in `src/Modules/Customers/Ashen.Customers/Data/`
- [X] T088 [US4] `AddressValidator` using `IPricingModule.GetActiveRegion` in `src/Modules/Customers/Ashen.Customers/Domain/AddressValidator.cs`
- [X] T089 [US4] Address slices list/add/edit/delete/set-default (profile created on first use) in `src/Modules/Customers/Ashen.Customers/Features/Addresses.cs`
- [X] T090 [US4] `CustomersModule` and `ICustomersModule.GetAddress` (returns `AddressSnapshot`) in `src/Modules/Customers/Ashen.Customers/CustomersModule.cs`, `CustomersModuleApi.cs`, contract types in `src/Modules/Customers/Ashen.Customers.Contracts/`
- [X] T091 [US4] `AccountDeletionRequested` handler deleting the profile in `src/Modules/Customers/Ashen.Customers/Features/OnAccountDeletionRequested.cs`
- [X] T092 [US4] Addresses page with `AddressForm` (state dropdown from region, field-level errors) in `web/src/features/addresses/AddressesPage.tsx` and `web/src/features/addresses/AddressForm.tsx`

**Checkpoint**: Customers have delivery addresses ready for checkout.

---

## Phase 7: User Story 5 - Cart and checkout with simulated payment (Priority: P5)

**Goal**: Cart (guest and signed in), checkout with quote (shipping + Illinois tax), simulated payment, order creation without over-selling or orphan charges, confirmation email.

**Independent Test**: Guest adds items → signs in (cart kept) → checks out to an IL address with `4242…` → order Paid, stock reduced, email in Mailpit; `…0002` declines with no order (quickstart #6–7, #15). Depends on US1, US2, US4.

### Tests for User Story 5 ⚠️

- [X] T093 [P] [US5] Unit tests for `Cart` (add/merge quantities, 1–99 bounds, remove at 0, guest merge) in `tests/Ashen.UnitTests/Ordering/CartTests.cs`
- [X] T094 [P] [US5] Unit tests for `Order` (total = subtotal − discount + shipping + tax; snapshots immutable; status transitions Paid→Shipped→Delivered, Paid→Cancelled, invalid ones fail; `CanCustomerCancel` true at 59 minutes, false at 61 minutes or once Shipped) in `tests/Ashen.UnitTests/Ordering/OrderTests.cs`
- [X] T095 [P] [US5] Unit tests for `SimulatedPaymentGateway` (test cards table in contracts/payment-gateway.md, Luhn, expiry, same idempotency key → same result, capture/void/refund) in `tests/Ashen.UnitTests/Payments/SimulatedPaymentGatewayTests.cs`
- [X] T096 [P] [US5] Unit tests for `TableTaxCalculator` (IL 6.25% of $100.00 = $6.25, missing state = $0.00, half-up rounding, latest effective rate wins) in `tests/Ashen.UnitTests/Pricing/TableTaxCalculatorTests.cs`
- [X] T097 [P] [US5] Integration tests in `tests/Ashen.IntegrationTests/Ordering/CheckoutTests.cs`: success → order Paid, stock reduced, `HasBeenOrdered` set, confirmation email captured; decline → 409, no order, stock and reservations released; quantity above stock → 409 with available count; guest cart merges into the customer cart on the first cart request after sign-in and the `ashen_cart` cookie is deleted; adding an out-of-stock, unpublished or unknown variant, or a quantity above available stock → 409 (FR-004); same idempotency key → one order; later price change leaves order unchanged; unpublished item blocks checkout; order save fails after payment approval (forced by a test-only `SaveChangesInterceptor` that throws on `Order` insert) → payment Voided, stock reservation and coupon released, no order (FR-021); an Authorized payment with no order older than 15 minutes is voided by `StaleAuthorizationVoider` (run directly with a fake `TimeProvider`)
- [X] T098 [P] [US5] Integration test for SC-007 in `tests/Ashen.IntegrationTests/Ordering/CheckoutConcurrencyTests.cs`: 100 parallel checkouts by different customers for a variant with stock 1 → exactly 1 order, 99 × 409, no captured or authorized-unvoided payments without an order
- [X] T099 [P] [US5] Component test for `CardEntry` (digits-only number with spacing, expiry MM/YY validation, CVC 3–4 digits, card number never written to `localStorage`/`sessionStorage`, emits values on submit) in `web/tests/unit/CardEntry.test.tsx`

### Implementation for User Story 5

- [X] T100 [US5] `StockReservation` entity, `Variant.StockReserved`, migration in `src/Modules/Catalog/Ashen.Catalog/Domain/StockReservation.cs` and `src/Modules/Catalog/Ashen.Catalog/Data/`
- [X] T101 [US5] Catalog contract methods `GetVariantsForCheckout`, `ReserveStock` (single conditional `UPDATE ... WHERE stock_on_hand - stock_reserved >= qty` per line inside one transaction), `ConfirmStock` (also sets `Product.HasBeenOrdered`), `ReleaseStock`, `RestockForCancelledOrder` in `src/Modules/Catalog/Ashen.Catalog/CatalogModuleApi.cs` and `src/Modules/Catalog/Ashen.Catalog.Contracts/ICatalogModule.cs`
- [X] T102 [US5] Background service releasing pending stock reservations older than 15 minutes in `src/Modules/Catalog/Ashen.Catalog/Background/ExpiredReservationReleaser.cs`
- [X] T103 [P] [US5] `ITaxCalculator`, `TableTaxCalculator`, and `IPricingModule.Quote` (shipping fee from StoreSettings, tax on merchandise after discount, shipping untaxed) in `src/Modules/Pricing/Ashen.Pricing/Tax/` and `src/Modules/Pricing/Ashen.Pricing/PricingModuleApi.cs`
- [X] T104 [P] [US5] `IPaymentGateway` and `SimulatedPaymentGateway` (stateless tokens, `Payments:SimulateLatencyMs`) per contracts/payment-gateway.md in `src/Modules/Payments/Ashen.Payments/Gateway/`
- [X] T105 [US5] `Payment` aggregate (Authorized, Captured, Voided, Refunded, Declined), `PaymentsDbContext` (schema `payments`), migration in `src/Modules/Payments/Ashen.Payments/`
- [X] T106 [US5] `IPaymentsModule` Authorize (with `NewCardSource`), Capture, Void, Refund and anonymous `GET /api/payments/mode` in `src/Modules/Payments/Ashen.Payments/PaymentsModuleApi.cs`, `src/Modules/Payments/Ashen.Payments/PaymentsModule.cs` (mode endpoint mapped inline), contract types in `src/Modules/Payments/Ashen.Payments.Contracts/`
- [X] T107 [US5] Background service voiding payments still Authorized with no `OrderId` after 15 minutes (covers a crash between authorization and order save, FR-021) in `src/Modules/Payments/Ashen.Payments/Background/StaleAuthorizationVoider.cs`
- [X] T108 [US5] `Cart`, `CartItem`, `Order`, `OrderLine`, `OrderStatus` domain; `OrderingDbContext` (schema `ordering`, sequence for `ASH-######` order numbers, unique `checkout_id`, which is the client's idempotency key; `xmin` concurrency on Order; `Order.CanCustomerCancel(now)` = Paid and within 1 hour of PlacedAt) + migration in `src/Modules/Ordering/Ashen.Ordering/Domain/` and `src/Modules/Ordering/Ashen.Ordering/Data/`
- [X] T109 [US5] Cart slices `GET /api/cart` (flags unavailable/price-changed lines) and `PUT /api/cart/items/{variantId}` (checks `ICatalogModule.GetVariantsForCheckout`: 409 for unknown, unpublished or out-of-stock variants or quantity above available, FR-004) with anonymous `ashen_cart` HttpOnly cookie token in `src/Modules/Ordering/Ashen.Ordering/Features/Cart.cs`
- [X] T110 [US5] `CartResolver` used by every cart and checkout slice: returns the customer's cart (or the guest cart from the `ashen_cart` cookie), and when a signed-in request still carries the cookie, merges the guest cart into the customer cart (quantities added, capped at 99) and deletes the cookie, in `src/Modules/Ordering/Ashen.Ordering/Shared/CartResolver.cs`
- [X] T111 [US5] `POST /api/checkout/quote` slice in `src/Modules/Ordering/Ashen.Ordering/Features/Checkout.cs`
- [X] T112 [US5] `POST /api/checkout` slice and `CheckoutOrchestrator` (reserve stock → authorize → save order → confirm stock → capture; compensation in reverse on any failure; uses the request's `idempotencyKey` as `CheckoutId`, and a repeat with the same key returns the original order or failure without re-running) per plan.md checkout sequence in `src/Modules/Ordering/Ashen.Ordering/Features/Checkout.cs` and `src/Modules/Ordering/Ashen.Ordering/Checkout/CheckoutOrchestrator.cs`
- [X] T113 [US5] `OrderPlaced` event and handler sending the confirmation email (store name and order link from `StoreOptions`) in `src/Modules/Ordering/Ashen.Ordering/Features/OnOrderPlaced.cs`
- [X] T114 [US5] `OrderingModule` registration in `src/Modules/Ordering/Ashen.Ordering/OrderingModule.cs`
- [X] T115 [P] [US5] Add-to-cart on product detail and cart count badge in `web/src/features/cart/AddToCartButton.tsx` and `web/src/features/cart/CartBadge.tsx`
- [X] T116 [US5] Cart page (quantities, remove, unavailable/price-changed notices, checkout button) in `web/src/features/cart/CartPage.tsx`
- [X] T117 [US5] Checkout page (address picker with re-quote on change, `CardEntry` component, `CheckoutSummary`, simulated-payment notice from `/api/payments/mode`, place order with an idempotency key generated once per attempt (reused on retry of the same request, regenerated after a decline or conflict), problem-details error display) in `web/src/features/checkout/CheckoutPage.tsx`, `web/src/features/checkout/CardEntry.tsx`, `web/src/features/checkout/CheckoutSummary.tsx`
- [X] T118 [US5] Order confirmation page in `web/src/features/checkout/OrderConfirmationPage.tsx`
- [X] T119 [P] [US5] Component test: `CheckoutSummary` renders subtotal, discount, shipping, tax, total formatted in USD in `web/tests/unit/CheckoutSummary.test.tsx`

**Checkpoint**: The store can take (simulated) orders safely.

---

## Phase 8: User Story 6 - Sales tax and regional settings (Priority: P6)

**Goal**: Admins manage per-state tax rates and the flat shipping fee; all money/dates display via region settings.

**Independent Test**: $100 cart → IL $6.25, TX $0.00; admin sets IL 7% → new quotes use 7%, old orders keep 6.25% (quickstart #8, SC-010, SC-011). Depends on US5 for checkout.

### Tests for User Story 6 ⚠️

- [X] T120 [P] [US6] Integration tests in `tests/Ashen.IntegrationTests/Pricing/TaxSettingsTests.cs`: quote IL vs TX; admin rate change applies to new quotes only; existing order unchanged; shipping fee change reflected; customer → 403 on admin pricing routes; audit entries written
- [X] T121 [P] [US6] Integration test table for SC-010 (≥20 checkouts across IL/non-IL, with/without coupon once US9 exists — start with no-coupon rows) comparing totals to hand-calculated values in `tests/Ashen.IntegrationTests/Pricing/TotalsAccuracyTests.cs`

### Implementation for User Story 6

- [X] T122 [US6] `GET /api/admin/tax-rates` and `PUT /api/admin/tax-rates/{jurisdictionCode}` (adds a new effective-dated row; validates 0 ≤ rate < 0.2 and known state) in `src/Modules/Pricing/Ashen.Pricing/Features/Admin/ManageTaxRates.cs`
- [X] T123 [P] [US6] `GET/PUT /api/admin/store-settings` (flat shipping fee) in `src/Modules/Pricing/Ashen.Pricing/Features/Admin/ManageStoreSettings.cs`
- [X] T124 [P] [US6] Admin tax & shipping page in `web/src/features/admin/settings/TaxAndShippingPage.tsx`

**Checkpoint**: Tax is admin-configurable per state; adding a state needs no code change.

---

## Phase 9: User Story 7 - Order history and fulfilment (Priority: P7)

**Goal**: Customers see their orders; admins ship, deliver, cancel (restock + simulated refund).

**Independent Test**: Place an order, admin marks Shipped → customer sees Shipped and gets email; cancel another → restocked and refunded (quickstart #10). Depends on US5.

### Tests for User Story 7 ⚠️

- [X] T125 [P] [US7] Integration tests in `tests/Ashen.IntegrationTests/Ordering/OrderFulfilmentTests.cs`: own orders newest first; other customer's order → 404; ship → email captured; deliver; invalid transition → 409; cancel restores stock and marks payment Refunded; customer cancels own order 30 minutes after placing → Cancelled, restocked, refunded; customer cancel after 1 hour or after Shipped → 409; customer cannot cancel another customer's order (404); parallel customer cancel and admin ship → exactly one succeeds, the other gets 409; customer → 403 on admin order routes; audit entries

### Implementation for User Story 7

- [X] T126 [US7] `OrderCancellation` shared by customer and admin cancel (status change under `xmin`, then `ICatalogModule.RestockForCancelledOrder` and `IPaymentsModule.Refund`, audit when admin) in `src/Modules/Ordering/Ashen.Ordering/Shared/OrderCancellation.cs`
- [X] T127 [US7] `GET /api/me/orders`, `GET /api/me/orders/{id}` (with `canCancel` from `TimeProvider`) and `POST /api/me/orders/{id}/cancel` (409 when too late or not Paid) in `src/Modules/Ordering/Ashen.Ordering/Features/MyOrders.cs`
- [X] T128 [US7] Admin order slices list (status filter, paging), detail, ship, deliver, cancel (via `OrderCancellation`) in `src/Modules/Ordering/Ashen.Ordering/Features/Admin/ManageOrders.cs`
- [X] T129 [US7] `OrderShipped` handler sending the shipping email (store name and order link from `StoreOptions`) in `src/Modules/Ordering/Ashen.Ordering/Features/OnOrderShipped.cs`
- [X] T130 [US7] `AccountDeletionRequested` handler removing personal data from orders (recipient name, phone, street lines replaced; totals kept) in `src/Modules/Ordering/Ashen.Ordering/Features/OnAccountDeletionRequested.cs`
- [X] T131 [P] [US7] Order history and order detail pages (Cancel button with confirmation when `canCancel`, conflict message if it has just shipped) in `web/src/features/orders/OrderHistoryPage.tsx` and `web/src/features/orders/OrderDetailPage.tsx`
- [X] T132 [P] [US7] Admin orders list and detail with ship/deliver/cancel actions in `web/src/features/admin/orders/OrdersAdminPage.tsx` and `web/src/features/admin/orders/OrderAdminDetailPage.tsx`

**Checkpoint**: The store can be run day to day.

---

## Phase 10: User Story 8 - Saved payment cards (Priority: P8)

**Goal**: Customers save, list, default, and remove simulated cards; checkout can use a saved card.

**Independent Test**: Save `5555…4444`; only brand/last4/expiry shown; delete it; database has no full number (quickstart #13, SC-009). Depends on US5.

### Tests for User Story 8 ⚠️

- [X] T133 [P] [US8] Integration tests in `tests/Ashen.IntegrationTests/Payments/SavedCardTests.cs`: save → masked fields only; declined/invalid card not saved; expired card flagged and rejected at checkout; default switching; other customer's card → 404; checkout with saved card succeeds; scan all `payments` schema text columns proves no full card number or CVC stored (SC-009)

### Implementation for User Story 8

- [X] T134 [US8] `SavedCard` entity + migration in `src/Modules/Payments/Ashen.Payments/Domain/SavedCard.cs` and `src/Modules/Payments/Ashen.Payments/Data/`
- [X] T135 [US8] Card slices `GET/POST /api/me/cards`, `POST /api/me/cards/{id}/default`, `DELETE /api/me/cards/{id}` in `src/Modules/Payments/Ashen.Payments/Features/SavedCards.cs`
- [X] T136 [US8] Support `SavedCardSource` in `IPaymentsModule.Authorize` and implement `GetCard` in `src/Modules/Payments/Ashen.Payments/PaymentsModuleApi.cs`
- [X] T137 [US8] `AccountDeletionRequested` handler deleting saved cards in `src/Modules/Payments/Ashen.Payments/Features/OnAccountDeletionRequested.cs`
- [X] T138 [P] [US8] Payment methods page (with the "no real payment is taken" notice from `/api/payments/mode`, FR-028) in `web/src/features/payment-methods/PaymentMethodsPage.tsx`
- [X] T139 [US8] Checkout: choose saved card (expired disabled) or enter new card with "save this card" option in `web/src/features/checkout/CheckoutPage.tsx`

**Checkpoint**: Repeat checkout uses saved cards.

---

## Phase 11: User Story 9 - Discount coupons (Priority: P9)

**Goal**: Admins create coupons; shoppers apply one per order; limits hold under concurrency.

**Independent Test**: `WELCOME10` on $50 → $5 off, tax after discount; expired code refused (quickstart #9). Depends on US5.

### Tests for User Story 9 ⚠️

- [X] T140 [P] [US9] Unit tests for `Coupon` (percentage, fixed capped at subtotal, minimum spend, date window, total limit, one-per-customer, inactive) in `tests/Ashen.UnitTests/Promotions/CouponTests.cs`
- [X] T141 [P] [US9] Integration tests in `tests/Ashen.IntegrationTests/Promotions/CouponApiTests.cs`: apply/replace/remove on cart; expired/min-spend refusal reasons; tax computed after discount at checkout; two parallel checkouts with a limit-1 coupon → exactly one discounted order; failed payment releases the redemption; customer → 403 on admin coupon routes
- [X] T142 [P] [US9] Add coupon rows to the SC-010 table in `tests/Ashen.IntegrationTests/Pricing/TotalsAccuracyTests.cs`

### Implementation for User Story 9

- [X] T143 [US9] `Coupon` aggregate (⚡ `xmin`) and `CouponRedemption`, `PromotionsDbContext` (schema `promotions`), migration, dev seed `WELCOME10` (10%, no limit) and an expired sample in `src/Modules/Promotions/Ashen.Promotions/`
- [X] T144 [US9] `IPromotionsModule` Validate, ReserveCoupon, ConfirmCoupon, ReleaseCoupon and background release of pending redemptions older than 15 minutes in `src/Modules/Promotions/Ashen.Promotions/PromotionsModuleApi.cs` and `src/Modules/Promotions/Ashen.Promotions/Background/ExpiredRedemptionReleaser.cs`, contract types in `src/Modules/Promotions/Ashen.Promotions.Contracts/`
- [X] T145 [US9] Admin coupon slices list/create/edit/deactivate in `src/Modules/Promotions/Ashen.Promotions/Features/Admin/ManageCoupons.cs` and `PromotionsModule` registration in `src/Modules/Promotions/Ashen.Promotions/PromotionsModule.cs`
- [X] T146 [US9] Cart coupon slices `PUT/DELETE /api/cart/coupon` in `src/Modules/Ordering/Ashen.Ordering/Features/CartCoupon.cs`; include discount in quote (`src/Modules/Ordering/Ashen.Ordering/Checkout/CheckoutPricing.cs`) and add reserve/confirm/release coupon steps to `src/Modules/Ordering/Ashen.Ordering/Checkout/CheckoutOrchestrator.cs`
- [X] T147 [P] [US9] Coupon input on cart and checkout in `web/src/features/cart/CouponInput.tsx`
- [X] T148 [P] [US9] Admin coupons page in `web/src/features/admin/coupons/CouponsPage.tsx`

**Checkpoint**: Promotions work without risk of over-redemption.

---

## Phase 12: User Story 10 - Product reviews (Priority: P10)

**Goal**: Verified buyers review products; ratings show on product pages and lists; admins moderate.

**Independent Test**: Customer with a Delivered order reviews (4 stars) → shown and average updated; non-buyer → 403; admin hide removes it from average (quickstart #11). Depends on US7 (Delivered status).

### Tests for User Story 10 ⚠️

- [X] T149 [P] [US10] Unit tests for `Review` (rating 1–5, text ≤ 2,000, hide/unhide) in `tests/Ashen.UnitTests/Reviews/ReviewTests.cs`
- [X] T150 [P] [US10] Integration tests in `tests/Ashen.IntegrationTests/Reviews/ReviewApiTests.cs`: verified buyer can create; second submit updates; non-buyer → 403; hidden excluded from list and average; product list includes rating and `sort=rating` works; customer → 403 on moderation routes

### Implementation for User Story 10

- [X] T151 [US10] `IOrderingModule.HasDeliveredProduct` in `src/Modules/Ordering/Ashen.Ordering/OrderingModuleApi.cs` and `src/Modules/Ordering/Ashen.Ordering.Contracts/IOrderingModule.cs`
- [X] T152 [US10] `Review` aggregate, `ReviewsDbContext` (schema `reviews`, unique product+customer), migration in `src/Modules/Reviews/Ashen.Reviews/`
- [X] T153 [US10] Review slices `GET /api/products/{productId}/reviews`, `PUT/DELETE .../reviews/mine`, admin list/hide/unhide in `src/Modules/Reviews/Ashen.Reviews/Features/Reviews.cs` and `src/Modules/Reviews/Ashen.Reviews/Features/Admin/ModerateReviews.cs`, and `ReviewsModule` registration
- [X] T154 [US10] `IReviewsModule.GetRatings` in `src/Modules/Reviews/Ashen.Reviews/ReviewsModuleApi.cs`; include rating summary and `sort=rating` in `src/Modules/Catalog/Ashen.Catalog/Features/BrowseProducts.cs` and `GetProduct.cs`
- [X] T155 [US10] `AccountDeletionRequested` handler setting author to "Former customer" in `src/Modules/Reviews/Ashen.Reviews/Features/OnAccountDeletionRequested.cs`
- [X] T156 [P] [US10] Reviews section and review form on product detail, star rating on `ProductCard` in `web/src/features/reviews/ProductReviews.tsx` and `web/src/features/reviews/StarRating.tsx`
- [X] T157 [P] [US10] Admin review moderation page in `web/src/features/admin/reviews/ReviewsAdminPage.tsx`

**Checkpoint**: Social proof on product pages.

---

## Phase 13: User Story 11 - Wishlist (Priority: P11)

**Goal**: Signed-in customers save products, remove them, and move them to the cart.

**Independent Test**: Add two, remove one, move the other to the cart (quickstart #12). Depends on US1, US2, US4 (Customers module); moving to cart needs US5.

### Tests for User Story 11 ⚠️

- [X] T158 [P] [US11] Integration tests in `tests/Ashen.IntegrationTests/Customers/WishlistApiTests.cs`: add is idempotent; remove; anonymous → 401; unpublished product shown as unavailable

### Implementation for User Story 11

- [X] T159 [US11] Wishlist slices `GET /api/me/wishlist` (product summaries + availability via `ICatalogModule.GetProductSummaries`), `PUT/DELETE /api/me/wishlist/{productId}` in `src/Modules/Customers/Ashen.Customers/Features/Wishlist.cs`
- [X] T160 [P] [US11] Wishlist toggle on product detail and `ProductCard` (prompts sign-in when anonymous) in `web/src/features/wishlist/WishlistButton.tsx`
- [X] T161 [US11] Wishlist page with "move to cart" (choose a variant with the shared `VariantSelector` from `web/src/features/catalog/components/`) in `web/src/features/wishlist/WishlistPage.tsx`

**Checkpoint**: Wishlist works.

---

## Phase 14: User Story 12 - Admin manages admins (Priority: P12)

**Goal**: Admins grant and revoke the Admin role; the last admin cannot be removed.

**Independent Test**: Promote a customer → they reach admin pages; revoke; last admin self-revoke → 409 (quickstart #14). Depends on US2.

### Tests for User Story 12 ⚠️

- [X] T162 [P] [US12] Integration tests in `tests/Ashen.IntegrationTests/Identity/AdminRoleTests.cs`: grant by email; unknown email → 404; revoke; last admin → 409; customer → 403; audit entries

### Implementation for User Story 12

- [X] T163 [US12] `GET/POST /api/admin/admins` and `DELETE /api/admin/admins/{userId}` in `src/Modules/Identity/Ashen.Identity/Features/Admin/ManageAdmins.cs`
- [X] T164 [P] [US12] Admins page in `web/src/features/admin/admins/AdminsPage.tsx`

**Checkpoint**: All 12 stories complete.

---

## Phase 15: Polish & Cross-Cutting Concerns

- [X] T165 [P] Multi-stage `Dockerfile` (Node 22 builds `web/` → `dotnet publish src/Ashen.Api` with `web/dist` copied to `wwwroot`, non-root user, port 8080) and `.dockerignore` at repo root
- [X] T166 [P] Playwright config and end-to-end smoke test (browse → add to cart → sign in → checkout with `4242…` → confirmation) run in two Playwright projects, desktop (1280×800) and mobile (375×812, touch), for FR-046, in `web/playwright.config.ts` and `web/tests/e2e/checkout.spec.ts`
- [X] T167 [P] Accessibility checks with `@axe-core/playwright` on home, product list, product detail, cart, checkout, sign-in, in both desktop and mobile projects (no horizontal scrolling at 375 px), in `web/tests/e2e/a11y.spec.ts`; fix violations found
- [X] T168 [P] Performance check for SC-005: optional seed `Catalog:Seed:LargeCatalog=1000` in `src/Modules/Catalog/Ashen.Catalog/Data/CatalogSeeder.cs` and an integration test asserting product list/detail queries stay under 2 s at p95 over 100 requests in `tests/Ashen.IntegrationTests/Catalog/CatalogPerformanceTests.cs`
- [X] T169 [P] Load test for SC-006: k6 script (run via the `grafana/k6` Docker image, nothing to install) with 200 virtual users browsing home → product list → product detail for 5 minutes against the container from T165, thresholds p95 < 2 s and error rate < 1%, in `tests/load/browse.js`, with run instructions in `README.md`
- [X] T170 [P] Integration test for account deletion across modules (profile and addresses removed, saved cards deleted, reviews show "Former customer", order personal data stripped while totals remain, sign-in refused) in `tests/Ashen.IntegrationTests/Platform/AccountDeletionTests.cs`
- [X] T171 Run every scenario in `specs/001-ashen-foundation/quickstart.md`, fix gaps, and update `README.md` with anything that changed
- [X] T172 Run `/speckit-analyze` for cross-artifact consistency before calling the feature done

---

## Contract Coverage (constitution II/VII)

Every public module contract is exercised by at least one integration test:

| Contract | Methods / events | Covered by |
|---|---|---|
| `ICatalogModule` | GetVariantsForCheckout, ReserveStock, ConfirmStock, ReleaseStock | T097, T098 |
| | RestockForCancelledOrder | T125 |
| | GetProductSummaries | T158 |
| `IPricingModule` | GetActiveRegion | T084 |
| | Quote | T097, T120 |
| `ICustomersModule` | GetAddress | T097 |
| `IPaymentsModule` | Authorize, Capture, Void | T097 |
| | Refund | T125 |
| | GetCard, Authorize with saved card | T133 |
| `IPromotionsModule` | Validate, ReserveCoupon, ConfirmCoupon, ReleaseCoupon | T141 |
| `IOrderingModule` | HasDeliveredProduct | T150 |
| `IReviewsModule` | GetRatings | T150 |
| `ICurrentUser` | all | T032 and the 403/404 checks in each story's tests |
| Events | `OrderPlaced`, `OrderShipped` | T097, T125 |
| | `AccountDeletionRequested` (all four handlers) | T170 |

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)** → **Foundational (Phase 2)** → user stories → **Polish (Phase 15)**

### User story dependencies

```text
US1 Browse ─┬─► US3 Admin catalog ◄── US2 Accounts ──► US12 Admins
            │                          │
            └──────────┬───────────────┤
                       ▼               ▼
                 US4 Addresses ──► US5 Cart & checkout ─┬─► US6 Tax settings
                       │                                ├─► US7 Orders ──► US10 Reviews
                       │                                ├─► US8 Saved cards
                       │                                └─► US9 Coupons
                       └──────────────────────────────────► US11 Wishlist (move-to-cart needs US5)
```

- US1 and US2 can proceed in parallel right after Foundational.
- US3, US4, US12 need US2 (sign-in); US3 also needs US1.
- US5 needs US1, US2, US4. US6, US7, US8, US9 each need only US5 and can run in parallel.
- US10 needs US7 (Delivered orders). US11 needs US4's Customers module and US5 for move-to-cart.

### Within each story

- Tests first (they fail), then domain → data/migration → contract implementations → slices →
  front end. Tasks touching the same file run in sequence.

## Parallel Examples

```text
# Foundational building blocks together:
T012 Money · T013 Money tests · T014 AggregateRoot/Result · T015 Event bus · T016 Audit · T018 Web helpers · T019 Email

# US1 after Foundational:
T039 Product tests · T040 Browse tests · T041 Category · T042 Product aggregate · T044 File storage
then T051 ProductCard · T052 HomePage while back-end slices T046–T049 are built

# US5 independent pieces:
T093–T099 tests · T103 tax calculator · T104 simulated gateway · T115 add-to-cart button

# After US5, four stories in parallel:
US6 (T120–T124) · US7 (T125–T132) · US8 (T133–T139) · US9 (T140–T148)
```

## Implementation Strategy

### MVP first

1. Phase 1 Setup → Phase 2 Foundational.
2. Phase 3 **US1 Browse** → stop and validate with quickstart #1. This is the first demo:
   a browsable store with seeded products.

### Incremental delivery

1. **+ US2 Accounts, US3 Admin catalog**: the owner can run the catalog.
2. **+ US4 Addresses, US5 Checkout**: the first complete shopping loop. This is the real MVP of
   the store.
3. **+ US6 Tax settings, US7 Orders**: day-to-day operation.
4. **+ US8 Saved cards, US9 Coupons, US10 Reviews, US11 Wishlist, US12 Admins**: polish features.
5. **Phase 15 Polish**: container, end-to-end tests, accessibility, performance.

### Learning-friendly pacing

Each story is a natural learning unit:

- **US1**: EF Core basics, React lists and routing.
- **US2**: ASP.NET Identity and cookies, React forms.
- **US5**: aggregates, concurrency and cross-module orchestration, React server state with
  TanStack Query.

Commit after each checkpoint, but only when the owner asks.
