# Ordering System

[![CI](https://github.com/ahmadiss12/Ordering_System/actions/workflows/ci.yml/badge.svg)](https://github.com/ahmadiss12/Ordering_System/actions/workflows/ci.yml)

A multi-restaurant food ordering marketplace for Lebanon. Restaurants handle their own
delivery; the platform takes a commission and settles it in both directions depending on
whether the customer paid cash or online.

**Status:** Phase 1 complete. Phase 2 in progress — menu API and image upload done, Angular
workspace scaffolded and talking to the API — toolchain, skeleton, solution, the 26
entities, EF configuration, the initial migration, seed data, authentication, tenant
isolation and CI, plus the public and staff menu APIs with image upload. 99 backend tests
and 2 frontend tests pass. The database builds, the API
starts, and there are three restaurants with real menus in it.

## Documentation

| Document | What it covers |
|---|---|
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | 19 architecture decisions, each with the alternatives and why they lost |
| [`docs/DOMAIN-MODEL.md`](docs/DOMAIN-MODEL.md) | The 24 entities as class diagrams, and why one multi-tenant platform rather than a copy per restaurant |

## Stack

| Part | Choice |
|---|---|
| API | ASP.NET Core on .NET 10 (LTS), Clean Architecture |
| Database | SQL Server, EF Core code-first |
| Web | Angular — customer storefront and staff/admin dashboard (Phase 2) |
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

`dotnet run --project src/OrderingSystem.Api -- --seed` fills an empty database with three
restaurants, 24 menu items, 10 Beirut delivery zones and these accounts. Password for all:
`Passw0rd!` — development data only.

| Email | Role |
|---|---|
| `admin@ordering.test` | Platform admin |
| `owner@frieslab.test` | Owner, FriesLab |
| `staff@frieslab.test` | Staff, FriesLab |
| `owner@mezze.test` | Owner, Beirut Mezze House |
| `rita@example.test` | Customer, two saved addresses |
| `joe@example.test` | Customer |

Safe to run repeatedly: every insert is guarded, and ids are derived from names rather than
random, so re-seeding does not move anything.

## Tests

```bash
cd api
dotnet test OrderingSystem.slnx
```

93 tests:

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

The integration tests start a real SQL Server per run through Testcontainers. The EF in-memory
provider is deliberately not used: it enforces no unique index, no check constraint and no
concurrency token, so those tests would pass whether the code was right or wrong.

CI runs the same commands on every push, plus `dotnet list package --vulnerable`, which is how a
newly disclosed advisory in a package we already depend on becomes visible rather than waiting
for someone to notice.
