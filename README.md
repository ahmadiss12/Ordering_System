# Ordering System

A multi-restaurant food ordering marketplace for Lebanon. Restaurants handle their own
delivery; the platform takes a commission and settles it in both directions depending on
whether the customer paid cash or online.

**Status:** Phase 1 (foundation), steps 0–2 complete — toolchain, repository skeleton and
solution scaffold. No domain code yet.

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
- Node 22+ (from Phase 2 onward)

Solution files use the `.slnx` format, which needs Visual Studio 2022 17.14+, Rider 2025.1+,
or the `dotnet` CLI. Any editor works via the CLI.

## Getting started

```bash
cp .env.example .env            # then set a password that meets SQL Server's complexity rules
docker compose -f docker/docker-compose.yml up -d

cd api
dotnet build OrderingSystem.slnx
dotnet test  OrderingSystem.slnx
```

Mailpit's web UI is at http://localhost:8025 — every email the API sends in development is
caught there instead of reaching a real inbox.

## Layout

```
api/          ASP.NET Core solution (Domain / Application / Infrastructure / Api + tests)
web/          Angular workspace                          — Phase 2
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
