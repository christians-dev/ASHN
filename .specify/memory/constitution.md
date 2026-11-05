<!--
Sync Impact Report
- Version change: 1.1.0 → 1.1.1 (PATCH: front-end linter named as oxlint instead of ESLint;
  the Vite template and TypeScript 7 no longer support ESLint's TypeScript plugin. Rule unchanged.)
- Previous: 1.0.0 → 1.1.0 (MINOR: new principles and materially expanded guidance;
  nothing removed or redefined)
- Modified principles:
  IV. Simplicity First (KISS / YAGNI) → IV. Simplicity, Conventions & Proportionality
      (adds: follow framework conventions; apply principles proportionally)
  VI. Secure by Default (expanded: secure-coding rules, security headers, dependency scanning)
  VII. Tested Where It Matters (unchanged text; now also covers failure paths, see VIII)
- Added principles:
  VIII. Reliable Under Concurrency & Failure
  IX. Observable & Configurable
  X. Tenant-Ready, Single-Tenant
- Added sections: none (Technology Stack, Workflow and Governance amended in place)
- Removed sections: none
- Templates:
  ✅ .specify/templates/plan-template.md (Constitution Check gates read from this file; no edit needed)
  ✅ .specify/templates/spec-template.md (no constitution-driven sections; no edit needed)
  ✅ .specify/templates/tasks-template.md (test note still matches Principle VII; no edit needed)
  ✅ CLAUDE.md (points at the current plan; no edit needed)
  ⚠ specs/001-ashen-foundation/plan.md (Constitution Check table lacks rows VIII–X)
  ⚠ specs/001-ashen-foundation/tasks.md (needs tasks for options validation, security headers,
    dependency scanning, logging hygiene; see summary)
- Deferred TODOs: none
-->

# Ashen Constitution

Ashen is an online apparel store (React single-page front end, .NET back end, PostgreSQL).
It is one store today, built so it could later host several stores (Principle X).
It is also a learning project: the code MUST stay readable enough to learn from.

## Core Principles

### I. Modular Monolith of Vertical Slices

- The back end is ONE deployable application made of business modules: Catalog, Identity,
  Customers, Ordering (cart, checkout, orders), Payments, Pricing & Tax, Promotions, Reviews.
  New modules require a constitution-checked plan.
- Inside a module, code is organised by feature (vertical slice), not by technical layer:
  each use case (e.g. `PlaceOrder`, `PublishProduct`) keeps its endpoint, request/response,
  validation and handler together in one folder.
- There are no project-wide `Controllers/`, `Services/`, or `Repositories/` layers.
  Code shared by several slices of the same module lives in that module's `Domain` or
  `Shared` folder.

**Rationale**: slices keep each feature easy to find and change; modules keep the door open
to splitting a module into its own service later without a rewrite.

### II. Module Autonomy & Explicit Contracts

- Each module owns its data: its own database schema and its own EF Core `DbContext`.
  A module MUST NOT read or write another module's tables, and MUST NOT join across schemas.
- Modules talk to each other only through the other module's public contracts
  (a small contracts assembly with interfaces and DTOs) or through in-process integration
  events. Everything else in a module is `internal`.
- Cross-module references to entities are by ID only, never by navigation property.
- Module boundaries MUST be enforced by automated architecture tests that fail the build.

**Rationale**: these rules are what make "split it out later" real instead of aspirational.

### III. Ports for External Capabilities (SOLID at the seams)

- Anything that talks to the outside world or is expected to be swapped MUST sit behind
  an interface owned by the module that needs it. At minimum: payment gateway
  (`IPaymentGateway`, implemented in phase 1 by `SimulatedPaymentGateway`), tax calculation,
  email sending, file/image storage, external sign-in, and the system clock.
- Replacing an implementation (e.g. simulated → real payment provider, flat state rate →
  external tax service) MUST require only a new implementation and a registration change.
- Regional behaviour (country, currency, address rules, display formats, tax jurisdictions)
  MUST come from configuration/data, never from hard-coded `if (country == "US")` branches
  in business logic.
- Classes follow SOLID: one reason to change, depend on abstractions at the seams above,
  small focused interfaces.

**Rationale**: the store launches with simulated payments and one tax state; growth must
not mean rewriting checkout.

### IV. Simplicity, Conventions & Proportionality

- Interfaces are introduced only at real seams (Principle III) or where a test needs one.
  No interface-per-class, no generic repository over EF Core, no unit-of-work wrapper
  over `DbContext`.
- No microservices, message brokers, separate read databases, or event sourcing unless
  a plan demonstrates a measured need.
- Use the established conventions of the frameworks before inventing anything: ASP.NET Core
  dependency injection, middleware, the Options pattern, ProblemDetails, `ILogger`,
  `TimeProvider`; EF Core migrations and change tracking; React hooks and components.
  A custom mechanism needs a reason the convention cannot meet.
- Every new third-party package MUST be justified in the plan; prefer what .NET, ASP.NET Core,
  EF Core, React and the browser already provide.
- Simple CRUD slices stay simple; rich domain modelling is reserved for areas with real rules
  (Principle V).
- These principles are guidance applied with judgment, not a checklist of enterprise features
  to build. Effort MUST be proportional to the risk: heavy rigour where money, security or
  data integrity is at stake; the simplest thing that works elsewhere.

**Rationale**: a small team (and a learning one) pays for every layer of indirection.

### V. Correct Money, Tax & Domain Invariants

- Money is a value object holding a `decimal` amount and an ISO 4217 currency; floating point
  types MUST NOT hold money. Rounding is to the cent, half-up, at line and order totals.
- Aggregates with business rules (Product with Variants, Cart, Order, Coupon, Review) protect
  their own invariants: state changes go through methods on the aggregate root, not public
  setters, and an aggregate is saved in one transaction.
- Orders snapshot everything that can change later (prices, names, address, tax rate,
  discount).
- Database constraints (unique keys, non-null, check constraints, foreign keys within a module)
  back up the domain rules so invalid data cannot be stored even by a bug.

**Rationale**: money and stock errors are the bugs a shop cannot afford.

### VI. Secure by Default

- The React app never holds access tokens: the .NET back end acts as Backend-for-Frontend,
  using secure, HTTP-only, SameSite cookies, with anti-forgery protection on state-changing
  requests.
- Exactly two roles: Customer and Admin. Every endpoint declares its authorization
  explicitly; anonymous access is opt-in per endpoint. Customers can only reach their own data.
- Full card numbers and security codes are never stored or logged.
- Secrets never live in the repository; local development uses user secrets or environment
  variables, production uses a secret store.
- All input is validated at the API boundary; errors return RFC 9457 problem details
  without internal details.
- Secure coding: database access only through EF Core or parameterised SQL
  (`FromSql` with interpolation, never string concatenation); no `dangerouslySetInnerHTML`
  with user content; uploads checked by content, not file name; redirects only to local URLs.
- The host sends security headers (HSTS, Content-Security-Policy, `X-Content-Type-Options`,
  `Referrer-Policy`), and CI fails on known-vulnerable .NET or npm dependencies of high or
  critical severity.

**Rationale**: auth and payment data mistakes are the hardest to undo.

### VII. Tested Where It Matters

- MUST have automated tests: domain rules (money, tax, coupons, stock, order status),
  authorization rules, each module's public contracts, and module boundaries (Principle II).
- Slice handlers that touch the database are covered by integration tests against a real
  PostgreSQL instance (containerised), not an in-memory substitute.
- Front end: tests for components with logic (checkout totals, forms) and at least one
  end-to-end smoke test of browse → cart → checkout.
- Failure and concurrency paths named in Principle VIII are tested, not only the happy path.
- Trivial mapping/CRUD code needs no dedicated test. Tests MUST pass before merge.

**Rationale**: focus test effort on the code where a bug costs money or trust.

### VIII. Reliable Under Concurrency & Failure

- Shared counters (stock, coupon usage) MUST be protected against concurrent updates
  (optimistic concurrency or a single conditional update), and the losing request gets a
  clear conflict response, not a silent overwrite.
- Operations a user or network can repeat (placing an order, payments) MUST be idempotent.
- A workflow that spans modules MUST leave no half-finished state that costs money: each step
  is undone in reverse on failure, and anything that can be orphaned by a crash (reservations,
  payment authorisations) expires and is cleaned up by a background job.
- Calls to external systems have timeouts; retries are used only for idempotent operations.
- Unexpected errors are logged once with context and return a generic problem response;
  exceptions are not used for expected business outcomes (use `Result`).

**Rationale**: a shop is a concurrent system that handles money; partial failure is normal.

### IX. Observable & Configurable

- Logging is structured: `ILogger` with message templates (named placeholders, not string
  concatenation), correlated by the OpenTelemetry trace id, exported via ServiceDefaults.
- Logs MUST NOT contain secrets, passwords, tokens, card data, or full addresses; customer
  identity is logged by id, not by email or name.
- Each business-significant event (order placed, payment declined, admin change) is logged at
  Information; failures at Warning or Error with enough context to act on.
- Configuration uses the Options pattern with validation at start-up (`ValidateOnStart`),
  so a misconfigured app fails fast instead of failing on first use. Environment-specific
  values come from configuration, never from code branches on environment names in business
  logic.

**Rationale**: when something goes wrong in production, logs and config are all you have.

### X. Tenant-Ready, Single-Tenant

- Ashen serves one store. Do NOT build multi-tenancy now: no tenant ids, tenant resolution,
  or per-tenant databases until a spec asks for it.
- Do avoid choices that would block it later: store-wide settings (name, region, tax, shipping,
  branding, email sender) live in data or configuration, not constants or static state; no
  global mutable singletons holding store identity; URLs and emails take the store's base URL
  from configuration.
- If multi-tenancy is ever specified, tenant isolation becomes a MUST for every table, query,
  cache, file path and background job, and requires a MAJOR constitution amendment.

**Rationale**: SaaS is a possible future, not the current product; keeping the door open is
cheap, building the house now is not.

## Technology Stack & Constraints

- **Back end**: .NET 10 (LTS), C# 14, ASP.NET Core minimal APIs, EF Core 10 with code-first
  migrations per module, PostgreSQL. Nullable reference types on; warnings as errors.
- **Front end**: React single-page app built with Vite, written in TypeScript; served by or
  alongside the .NET host. Accessibility target: WCAG 2.2 AA. Visual design is decided per
  feature.
- **Repository**: one monorepo holding the back end, front end, tests and specs.
- **Runtime**: everything runs locally with a single command (containers for PostgreSQL and
  other dependencies). Production target is containers on AWS (ECS); no feature may depend on
  a cloud-specific service without an interface (Principle III).
- **Payments**: phase 1 uses `SimulatedPaymentGateway` only; no real money moves.
- **Market**: United States, US dollars, sales tax by state; configuration-driven so other
  regions can be added.

## Development Workflow & Quality Gates

- Work follows the Spec Kit flow: specify → clarify (if needed) → plan → tasks → implement.
  Every plan's Constitution Check MUST pass, or list each violation with justification in
  its Complexity Tracking table.
- Every change MUST build cleanly, pass formatting/linting (`dotnet format`, oxlint, Prettier),
  the dependency vulnerability check, and all tests before merge.
- Database schema changes only through EF Core migrations, one migration set per module.
- Commits and pushes happen only when the owner explicitly asks.
- Code is written to be read by a learner: clear names, small methods, comments only for
  the "why".

## Governance

- This constitution overrides other practices and templates. Plans, tasks and reviews MUST
  check compliance with it, applying Principle IV's proportionality: a gate is met when the
  risk it guards against is handled, not when every possible mechanism exists.
- Amendments: propose the change, record it in the Sync Impact Report at the top of this file,
  update dependent templates, and bump the version.
- Versioning: MAJOR for removed or redefined principles; MINOR for new principles or
  materially expanded guidance; PATCH for wording and clarifications.
- Any deliberate deviation MUST be recorded in the relevant plan's Complexity Tracking table
  with the simpler alternative and why it was rejected.

**Version**: 1.1.1 | **Ratified**: 2026-11-03 | **Last Amended**: 2026-11-03
