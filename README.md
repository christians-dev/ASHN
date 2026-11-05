# Ashen

Ashen is an online apparel store: a React single-page app in front of a .NET 10 modular monolith
backed by PostgreSQL. It is also a learning project, so the code aims to be easy to read.

- What it does: [specs/001-ashen-foundation/spec.md](specs/001-ashen-foundation/spec.md)
- How it is built: [specs/001-ashen-foundation/plan.md](specs/001-ashen-foundation/plan.md)
- Engineering rules: [.specify/memory/constitution.md](.specify/memory/constitution.md)
- Run and validate everything: [specs/001-ashen-foundation/quickstart.md](specs/001-ashen-foundation/quickstart.md)

## Prerequisites

- .NET 10 SDK (pinned in `global.json`)
- Node.js 22 LTS
- Docker Desktop, running (PostgreSQL and Mailpit containers, and the integration tests)
- Aspire CLI (`dotnet tool install -g Aspire.Cli`), optional: `dotnet run --project src/Ashen.AppHost` also works

## Run

```bash
npm ci --prefix web
```

```bash
aspire run
```

The Aspire dashboard lists `postgres`, `mailpit`, `api` and `web`. Open the `web` URL to use the store,
and the `mailpit` UI to read the emails it sends.

## Test

```bash
dotnet test --solution Ashen.slnx
```

```bash
npm test --prefix web
```

The .NET suite includes the SC-005 performance check (`CatalogPerformanceTests`), which seeds 1,000 products and
asserts product list and page responses stay under 2 s at p95.

### End-to-end and accessibility (Playwright)

Runs in a desktop (1280×800) and a touch phone (375×812) browser against the running store, so start `aspire run`
first. The first time, install the test browser: `npx --prefix web playwright install chromium`.

```bash
npm run e2e --prefix web
```

`checkout.spec.ts` walks browse → cart → sign up → pay with `4242 4242 4242 4242` → confirmation;
`a11y.spec.ts` runs axe (WCAG 2.1 AA) on the main pages and fails if a page scrolls sideways at 375 px.
Each run registers new `e2e-…@test.local` accounts in your local database.

## Container

One image serves the SPA and the API on port 8080 as a non-root user (see `Dockerfile`):

```bash
docker build -t ashen .
```

It needs `ConnectionStrings__ashen`, `Ashen__Store__BaseUrl` and `Email__Smtp__Host` (plus `Ashen__MigrateOnStartup=true`
to create the schema). The load balancer health check is `GET /alive`. It trusts `X-Forwarded-*` headers, so expose
it only through the load balancer. Sign-in keys are stored in PostgreSQL, so any number of tasks share sessions.

### Load test (SC-006, k6)

Starts PostgreSQL and the image with a generated 1,000-product catalog, then runs 200 browsing shoppers for 5 minutes
(thresholds: p95 < 2 s, errors < 1%). Needs only Docker.

```bash
docker network create ashen-load
```

```bash
docker run -d --name ashen-load-db --network ashen-load -e POSTGRES_PASSWORD=load-test-only -e POSTGRES_DB=ashen postgres:17-alpine
```

```bash
docker run -d --name ashen-load-app --network ashen-load -p 8080:8080 -e ConnectionStrings__ashen="Host=ashen-load-db;Database=ashen;Username=postgres;Password=load-test-only" -e Ashen__MigrateOnStartup=true -e Ashen__Store__BaseUrl=http://localhost:8080 -e Email__Smtp__Host=unused.invalid -e Catalog__Seed__LargeCatalog=1000 ashen
```

```bash
docker run --rm --network ashen-load -v "$PWD/tests/load:/scripts" -e BASE_URL=http://ashen-load-app:8080 grafana/k6:1.0.0 run /scripts/browse.js
```

Clean up with `docker rm -f ashen-load-app ashen-load-db` and `docker network rm ashen-load`.
On Git Bash for Windows, prefix the k6 command with `MSYS_NO_PATHCONV=1` and use `$(pwd -W)` instead of `$PWD`.

## Repository layout

```text
src/Ashen.AppHost/        Aspire: starts everything locally
src/Ashen.ServiceDefaults/ OpenTelemetry, health checks, resilience
src/Ashen.Api/            the host / backend-for-frontend; serves the SPA and /api
src/Ashen.BuildingBlocks/ Money, Result, events, audit, data conventions
src/Modules/<Name>/       one module = Ashen.<Name> (internal) + Ashen.<Name>.Contracts (public)
tests/                    unit, integration (real PostgreSQL) and architecture tests
web/                      React + TypeScript app (Vite)
```

## Troubleshooting

- **API stuck in "Waiting" / Postgres logs `password authentication failed`**: the AppHost must run in the
  Development environment (its `Properties/launchSettings.json` sets this), otherwise it ignores its user secrets
  and generates a new database password on every start. If a volume was created with another password, remove it
  and run again; it is recreated and re-seeded: `docker volume rm ashen.apphost-91fb6e8f9b-postgres-data`
  (this deletes all local store data).

## Notes

- This repo's `nuget.config` uses nuget.org only, regardless of feeds configured on your machine.
- Front-end linting uses oxlint (the current Vite template default; TypeScript 7 is not yet supported
  by ESLint's TypeScript plugin) and Prettier for formatting.
- API types for the front end are generated from the running API with `npm run api:types --prefix web`.
