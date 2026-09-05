# Ordering System

[![CI](https://github.com/ahmadiss12/Ordering_System/actions/workflows/ci.yml/badge.svg)](https://github.com/ahmadiss12/Ordering_System/actions/workflows/ci.yml)

A multi-restaurant food ordering marketplace for Lebanon. Restaurants handle their own
delivery; the platform takes a commission and settles it in both directions depending on
whether the customer paid cash or online.

**Status:** Phases 1, 2 and 3 complete.

Phase 1 built the foundations: the solution, the 26 entities, EF configuration, the first
migration, seed data, authentication, tenant isolation and CI. Phase 2 made a menu editable — the
public and staff menu APIs, image upload, the Angular workspace, a TypeScript client generated
from the API's own OpenAPI document, and a dashboard with a working menu editor.

Phase 3 made it sell food. A customer builds a basket and places an order the server prices; a
kitchen sees it arrive without pressing anything, works it from a tablet with one press per step,
and can refuse it with a reason that reaches a report. Every status change is recorded with the
account that made it, and the transitions an order may make live in one table that a truth test
walks in full.

## Documentation

| Document | What it covers |
|---|---|
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | 19 architecture decisions, each with the alternatives and why they lost |
| [`docs/DOMAIN-MODEL.md`](docs/DOMAIN-MODEL.md) | The 24 entities as class diagrams, and why one multi-tenant platform rather than a copy per restaurant |
| [`docs/PROJECT-STRUCTURE.md`](docs/PROJECT-STRUCTURE.md) | What lives in each folder today, what lands there later, and where a new file belongs |
| [`docs/PHASE-2-PLAN.md`](docs/PHASE-2-PLAN.md) | The menu phase, and the decisions taken inside each step |
| [`docs/PHASE-3-PLAN.md`](docs/PHASE-3-PLAN.md) | The orders phase — state machine, pricing, checkout, live updates, the kitchen screens — and what each step found |

## Stack

| Part | Choice |
|---|---|
| API | ASP.NET Core on .NET 10 (LTS), Clean Architecture |
| Database | SQL Server, EF Core code-first |
| Web | Angular — staff dashboard (Phases 2–3); customer storefront (Phase 5) |
| Mobile | React Native / Expo (Phase 5) |
| Real-time | SignalR |

## Prerequisites

- .NET SDK 10.0.100 or later (pinned in `global.json`)
- Docker, for SQL Server and the development mail catcher
- Node 24.20.0 or later (pinned in `web/.nvmrc`) — Angular refuses to run on an older one

Solution files use the `.slnx` format, which needs Visual Studio 2022 17.14+, Rider 2025.1+,
or the `dotnet` CLI. Any editor works via the CLI.

## Getting started

```powershell
git clone -b claude/project-structure-requirements-vu98q6 https://github.com/ahmadiss12/Ordering_System.git
cd Ordering_System
copy .env.example .env          # then set a password meeting SQL Server's complexity rules

powershell -ExecutionPolicy Bypass -File scripts\verify.ps1
```

`verify.ps1` checks everything in one pass and says which piece failed rather than just
"build failed": prerequisites, containers, build, migrations, schema shape, tests, and the
API answering on `/health`. Run it first after cloning.

To do it by hand instead:

```bash
docker compose -f docker/docker-compose.yml up -d

cd api
dotnet build OrderingSystem.slnx
dotnet ef database update --project src/OrderingSystem.Infrastructure \
                          --startup-project src/OrderingSystem.Infrastructure
dotnet run  --project src/OrderingSystem.Api -- --seed   # demo data, safe to re-run
dotnet test OrderingSystem.slnx
dotnet run  --project src/OrderingSystem.Api
```

Mailpit's web UI is at http://localhost:8025 — every email the API sends in development is
caught there instead of reaching a real inbox.

## Layout

```
.github/      CI: build, test, and a dependency-advisory scan
api/          ASP.NET Core solution (Domain / Application / Infrastructure / Api + tests)
web/          Angular workspace: dashboard, storefront, 3 shared libraries
mobile/       React Native app                           — Phase 5
docker/       SQL Server and Mailpit for local development
docs/         Architecture and domain model
```

### Why the api/ projects are split this way

Dependencies point inward and nothing points back out:

```
Domain          no references at all — entities, order transitions, money rules
Application     → Domain                  use cases, DTOs, validators, interfaces
Infrastructure  → Application             EF Core, SQL Server, email, payments
Api             → Application, Infrastructure   controllers, auth, SignalR, DI
```

`OrderingSystem.Domain.csproj` is deliberately empty of references, and a test in
`Domain.Tests/Architecture` fails the build if one is ever added.

## Conventions

- Money is `decimal(10,2)` in USD. Never `float`. LBP is derived from a rate snapshotted
  onto each order.
- All timestamps are UTC; conversion happens at the client.
- Package versions are declared once in `api/Directory.Packages.props`. Project files
  reference packages without a version.
- Warnings are errors. This is how a vulnerable transitive package or an unhandled null
  gets caught at build time rather than in review.

## Seed accounts

`dotnet run --project src/OrderingSystem.Api -- --seed` fills an empty database with four
restaurants, 24 menu items, 10 Beirut delivery zones and these accounts. Password for all:
`Passw0rd!` — development data only.

| Email | Role |
|---|---|
| `admin@ordering.test` | Platform admin |
| `owner@frieslab.test` | Owner, FriesLab |
| `staff@frieslab.test` | Staff, FriesLab |
| `owner@mezze.test` | Owner, Beirut Mezze House |
| `owner@shawarma.test` | Owner, Shawarma Station — open around the clock |
| `owner@sajcorner.test` | Owner, Saj Corner — nothing set up yet, see below |
| `rita@example.test` | Customer, two saved addresses |
| `joe@example.test` | Customer |

Three of those restaurants are fully configured and listed. **Saj Corner deliberately is not**: no
hours, no delivery zones, no menu, and hidden from customers — the state a restaurant is in
between the platform creating it and its owner setting it up. Signing in as its owner is the
quickest way to see what a real onboarding looks like, and the `onboarding` end-to-end test walks
that path from there to a cooked order.

Safe to run repeatedly: every insert is guarded, and ids are derived from names rather than
random, so re-seeding does not move anything.

## Tests

```bash
cd api
dotnet test OrderingSystem.slnx
```

Deliberately no test count here. One went stale within a week and then disagreed with another
count further up the same file, which is worse than no number at all — the CI badge is the
honest answer to "does it pass".

| Covers | Where | Needs Docker |
|---|---|---|
| Toolchain and infrastructure config | `Domain.Tests/Architecture/RepositoryConventionTests` | no |
| The dependency rule between projects | `Domain.Tests/Architecture/DependencyRuleTests` | no |
| Enum numbering stability | `Domain.Tests/Architecture/DependencyRuleTests` | no |
| The query-filter bypass allowlist | `Domain.Tests/Architecture/QueryFilterBypassTests` | no |
| Password and email rules | `Application.Tests/Auth` | no |
| Schema, indexes and check constraints | `Api.IntegrationTests/Persistence` | yes |
| The application booting and serving | `Api.IntegrationTests/Startup` | yes |
| Seed data and its idempotency | `Api.IntegrationTests/Seed` | yes |
| Registration, login, rotation, reset | `Api.IntegrationTests/Auth` | yes |
| Tenant isolation, at the data layer | `Api.IntegrationTests/Tenancy` | yes |
| Opening hours, including windows past midnight | `Domain.Tests/Restaurants` | no |
| Public catalogue: browse, menu, item detail | `Api.IntegrationTests/Menu` | yes |
| Menu editing, and 403 across restaurants | `Api.IntegrationTests/Menu` | yes |
| Token refresh, sharing one exchange across requests | `web` — `auth` library | no |
| The login screen, guards and the shell's role split | `web` — `dashboard` | no |
| The menu store, item form and selection rules | `web` — `dashboard` | no |
| Sign in, add a dish, see it on the public menu | `web/e2e` — Playwright | yes |

The integration tests start a real SQL Server per run through Testcontainers. The EF in-memory
provider is deliberately not used: it enforces no unique index, no check constraint and no
concurrency token, so those tests would pass whether the code was right or wrong.

The front end has its own suites, which need Node rather than Docker:

```bash
cd web
npm ci
npx ng test dashboard --watch=false   # and storefront, auth, ui
npm run e2e                           # Playwright, needs the API running - see web/README.md
```

CI runs all of these on every push, plus `dotnet list package --vulnerable`, which is how a
newly disclosed advisory in a package we already depend on becomes visible rather than waiting
for someone to notice.

### If a test fails only on your machine

Almost always one of three things, in order of likelihood:

- **Docker is not running.** Everything marked "needs Docker" above fails at once, with a
  connection error rather than an assertion.
- **Node is too old.** The Angular CLI refuses to start rather than misbehaving; `nvm use` in
  `web/` reads the pinned version from `.nvmrc`.
- **Line endings.** Fixed by `.gitattributes`, but a clone made before it existed still has CRLF
  on disk. `git rm --cached -r . && git reset --hard` re-checks-out with the right endings.

## The generated API client

`web/projects/shared/api-client/src/lib/api-client.ts` is generated, not written. Regenerate it
after any change to the API surface:

```bash
./scripts/generate-api-client.sh          # or scripts\generate-api-client.ps1 on Windows
```

It needs neither a running API nor a database — the OpenAPI document is produced by building the
project. CI runs the same script and fails if the committed copy has drifted, so a forgotten
regeneration is caught in review rather than as an `undefined` in front of a user.
