# Architecture Decisions

**Project:** Food ordering & delivery marketplace (Lebanon)
**Owner:** Ahmad Ismail
**Status:** Architecture agreed — nothing built yet
**Supersedes:** technical conventions in `PROJECT-SPEC.md` §12

---

## 1. Context

This is a commercial product, not a portfolio exercise. That single fact drives most of what follows, and it changes the calculus in three specific ways:

1. **Licensing matters.** A library that is free for a hobby project may not be free for a product you sell. This eliminated two of the most popular .NET libraries — see ADR-04 and ADR-09.
2. **Support windows matter.** Shipping on a framework that leaves support during the build is not defensible to a paying client — see ADR-05.
3. **Correctness under concurrency matters.** Two staff members advancing the same order, a customer double-tapping checkout, an exchange rate changing mid-order. A demo never hits these; a real restaurant hits them in week one.

### Constraints taken as given

| Constraint | Source |
|---|---|
| Multi-tenant marketplace, many restaurants | Spec §2 |
| Restaurants deliver themselves — no driver fleet, no dispatch | Spec §2 |
| Cash + online payment, tracked independently of order status | Spec §2, §5 |
| USD stored, LBP derived from a snapshotted rate | Spec §7 |
| Three clients: Angular storefront, Angular dashboard, React Native mobile | Spec §1 |
| Commission settles in both directions | Spec §8 |
| Tenant isolation is the top security property | Spec §4 |
| English only | Decided this round |
| No tax | Decided this round |
| Self-service login, including password recovery | Decided this round |

---

## 2. Decision summary

| # | Area | Decision | Chief alternative rejected |
|---|---|---|---|
| 01 | Repository layout | Monorepo | Separate repo per client |
| 02 | Backend structure | Clean Architecture, 4 projects | Vertical slices; single project |
| 03 | HTTP layer | Controllers | Minimal APIs |
| 04 | Application layer | Injected services | MediatR / CQRS bus |
| 05 | Runtime | .NET 10 (LTS) | .NET 8 |
| 06 | Data access | `DbContext` behind an interface | Repository + Unit of Work |
| 07 | Multi-tenancy | Shared schema, row-level, query filters + write guards | Database per tenant |
| 08 | Validation | FluentValidation, called explicitly | DataAnnotations; auto-validation filter |
| 09 | Object mapping | LINQ projections + Mapperly | AutoMapper |
| 10 | Authentication | JWT access + rotating refresh tokens | Cookie sessions |
| 11 | Order lifecycle | Explicit transition table + rowversion | `switch` statements; Stateless library |
| 12 | Real-time | SignalR, groups derived from JWT | WebSockets by hand; polling |
| 13 | Scheduled work | `IHostedService` + `PeriodicTimer` | Hangfire; Quartz |
| 14 | API contract | Built-in OpenAPI → generated TS client | Hand-written client per app |
| 15 | Errors | RFC 9457 `ProblemDetails` via `IExceptionHandler` | Ad-hoc error shapes |
| 16 | Frontend workspace | Angular CLI workspace, 2 apps + 3 libs | Nx; two standalone projects |
| 17 | Frontend state | Signals + RxJS, no global store | NgRx |
| 18 | Testing | xUnit + Testcontainers (real SQL Server) | EF InMemory provider |
| 19 | Local infrastructure | Docker Compose | Local installs |

---

## 3. The decisions

### ADR-01 — Monorepo

**Decision.** One repository containing the API, both Angular apps, the mobile app, infrastructure, and docs.

**Why.** The three clients and the API share one contract. When an endpoint changes shape, the generated TypeScript client and every consumer must change with it — in a monorepo that is one commit and one CI run that either passes or fails as a unit. Split across four repos, the same change becomes four PRs merged in a careful order, and there is a window where the repos disagree about what the API looks like.

**Why not separate repos.** Independent versioning and deploy cadence per client is the reason to split — and it earns its cost when separate teams own separate clients. One or two developers on a shared contract get none of that benefit and all of the coordination overhead.

**Cost accepted.** CI must scope work by path so an Angular change doesn't rebuild the API.

---

### ADR-02 — Clean Architecture for the backend

**Decision.** Four projects, dependencies pointing strictly inward:

```
OrderingSystem.Domain          → no project references at all
OrderingSystem.Application     → Domain
OrderingSystem.Infrastructure  → Application, Domain
OrderingSystem.Api             → Application, Domain (+ Infrastructure at composition root only)
```

- **Domain** — entities, enums, the order transition table, money rules, domain exceptions. No EF attributes, no ASP.NET types, no I/O.
- **Application** — use cases, DTOs, validators, and the interfaces the outside world must satisfy (`IAppDbContext`, `IEmailSender`, `IPaymentGateway`, `ITenantContext`, `IClock`).
- **Infrastructure** — EF Core configuration and migrations, email, payment gateway, token storage.
- **Api** — controllers, auth wiring, SignalR hubs, middleware, DI composition.

**Why.** Four things in this system are genuine business logic rather than data shuffling: the order state machine, option-group min/max validation, the money and snapshot rules, and two-directional settlement. Every one of them is on the required test list. Clean Architecture puts all four in `Domain` or `Application`, where they can be tested with no database, no HTTP, and no mocking framework. Tests that need neither get written; tests that need a running server do not.

The second reason is specific to this being a sellable product: if it is ever handed to another developer or another shop, Clean Architecture is the layout an enterprise .NET team expects. Familiarity is a real asset when someone else has to maintain what you wrote.

**Why not vertical slices.** Genuinely good for APIs that are mostly independent CRUD features, and it avoids the "one thin use case spread across four projects" problem. It loses here because our invariants are *cross-cutting*: tenant isolation, snapshotting, and money rounding apply to every slice that touches an order. Enforced per slice, they get duplicated, and the day one slice forgets is the day restaurant A reads restaurant B's orders.

**Why not a single project with folders.** Nothing stops `Domain` logic from reaching for `DbContext` or `HttpContext` — the compiler permits it, so eventually someone does. The project boundary is what makes the dependency rule enforceable rather than aspirational.

**Cost accepted.** More ceremony for simple endpoints. Genuinely trivial reads (list delivery zones) will feel over-built. That is the price of the boundary holding where it matters.

---

### ADR-03 — Controllers, not Minimal APIs

**Decision.** MVC controllers with attribute routing.

**Why.** The surface is roughly sixty endpoints in clear resource groups, and access rules are the dominant concern: every restaurant-scoped route needs a role check *and* a tenant check. As an attribute on a controller, that is one declaration covering every action inside it — visible at the top of the file, impossible to forget on a new action. Controllers also give conventional OpenAPI grouping for free, which matters because the generated client's shape follows it.

**Why not Minimal APIs.** Lower ceremony and faster startup, and they are the better choice for a small focused service. At sixty endpoints they need endpoint groups and filters to stay organised — at which point you have rebuilt controllers with less tooling. The startup difference is irrelevant for a long-running API.

---

### ADR-04 — Plain services, no MediatR

**Decision.** Application use cases are ordinary classes registered in DI and injected into controllers. No mediator, no request/handler pairs.

**Why not MediatR — licensing.** MediatR 13.0.0 and later require a paid commercial licence. Earlier versions stay under their original open-source terms, and the current source is available under RPL-1.5, a reciprocal licence with copyleft obligations. For software you intend to sell, that is either a recurring per-team cost or a licence obligation to read carefully. Neither is worth paying for indirection.

**Why not MediatR — design.** Its real value is decoupling a sender from a handler it must not know about, plus pipeline behaviours. We have exactly one sender per handler — the controller — so the decoupling buys nothing, and the pipeline concerns (logging, validation, transactions) are already served by middleware and action filters. What it reliably adds is a layer where "go to definition" stops working.

**What we keep from CQRS.** The useful half, without the bus: reads project straight to DTOs with `Select` and `AsNoTracking`, writes load tracked entities and enforce invariants. Separate paths, separate models — no `IRequest<T>` required.

---

### ADR-05 — .NET 10 (LTS)

**Decision.** Target .NET 10.

**Why.** .NET 8's LTS support window closes around November 2026 — roughly three months from now. Starting a commercial build on a runtime that leaves support before the project is mature means an unbudgeted upgrade almost immediately, and a client asking "is this supported?" deserves a better answer than "for another quarter". .NET 10 is LTS with a window running to late 2028.

**Why not .NET 8.** Only reason would be a dependency that hasn't caught up. Nothing in this stack qualifies.

**Cost accepted.** None material — every API in the spec exists unchanged in .NET 10.

---

### ADR-06 — `DbContext` behind an interface, no repository layer

**Decision.** `Application` depends on `IAppDbContext`, which exposes `DbSet<T>` properties and `SaveChangesAsync`. `Infrastructure` implements it with the EF `DbContext`. Reusable query logic lives in `IQueryable<T>` extension methods.

**Why.** `DbContext` is already a Unit of Work and `DbSet<T>` is already a repository — wrapping them adds a layer that abstracts an abstraction. The generic repositories that usually result (`GetAll`, `GetById`) quietly destroy performance, because the only way to express "load this order with its lines, its options, and the customer's name" through them is to fetch entities and filter in memory. The interface gives testability without giving up `Include`, projection, split queries, or `AsNoTracking`.

**Where the package boundary actually falls.** `Application` does reference `Microsoft.EntityFrameworkCore` — it has to, in order for `IAppDbContext` to expose `DbSet<T>`. What it does *not* reference is the provider: `Microsoft.EntityFrameworkCore.SqlServer` lives only in `Infrastructure`. So `Application` knows an ORM exists but never which database is behind it, and `Domain` knows neither.

**Why not repository + unit of work.** The standard justification is swapping the persistence technology. We will not swap SQL Server, and if we did, the repository interfaces would leak EF semantics anyway — lazy loading, change tracking, `IQueryable` — and would not survive the swap.

**Cost accepted.** `Application` code is EF-flavoured, so an EF-specific mistake can be written there. Integration tests against real SQL Server (ADR-18) are what catch it.

---

### ADR-07 — Shared schema, row-level tenancy

**Decision.** One database, one schema. Tenant-owned tables carry `RestaurantId`. Isolation is enforced in three layers:

1. **EF global query filters** on every tenant-owned entity, reading a scoped `ITenantContext` populated from the JWT's `restaurant_id` claim.
2. **Explicit ownership checks on every write and every by-id read**, because query filters do not apply to `Find`, to raw SQL, or to anything that calls `IgnoreQueryFilters()`.
3. **Tests** asserting that restaurant A receives 403, not 404 and not an empty list, when touching restaurant B's resources.

**Why three layers.** Filters alone are a trap. They are invisible at the call site, so a developer cannot tell by reading a method whether protection is active, and a single `IgnoreQueryFilters()` — legitimately needed for platform-admin reporting — silently disables them. Explicit checks alone are worse: they rely on nobody ever forgetting. Together, the filter is the net and the explicit check is the assertion.

**Child tables need their own filters.** EF does not apply a parent's filter to its children, so
filtering `Order` protected `SELECT * FROM Orders` and did nothing at all for `SELECT * FROM
OrderLines` — which carries the item names and prices of every restaurant on the platform.
`OrderLine`, `OrderLineOption`, `OrderEvent`, `Payment`, `CartLine` and `CartLineOption` now
restate their parent's rule through a navigation. It costs a join on every child query, and the
alternative was a convention that holds only until somebody forgets it.

**`IgnoreQueryFilters` is allowlisted.** Two files legitimately need it — the seeder, which runs
with no signed-in user, and the login path, which reads `RestaurantStaff` to decide what the
tenant *is*. A test enumerates every other use and fails the build, and a second test removes
allowlist entries once they stop being needed.

**The known footgun.** A platform admin must see across all tenants, so `ITenantContext.RestaurantId` is null for that role and the filter short-circuits. This means *the security of every tenant-scoped query depends on that one null check being right*. It gets its own dedicated tests, and no other code path is allowed to construct an admin-scoped context.

**Why not database per tenant.** Strongest possible isolation, and the usual choice when tenants demand it contractually. It fails here on a first-class requirement: the platform admin's cross-restaurant reports and settlement runs would have to fan out across N databases and merge in application code. It also multiplies migration risk by N.

**Why not schema per tenant.** All the cross-tenant query pain, without the isolation benefit, plus DDL on every restaurant onboarding.

---

### ADR-08 — FluentValidation, invoked explicitly

**Decision.** Validators live in `Application` beside their use case, and are called explicitly at the top of each one. Not registered as an automatic MVC filter.

**Why.** Our hardest rules are conditional and reach beyond a single field — "the selected options satisfy every attached group's min and max", "the cart's restaurant matches the order's restaurant", "the address belongs to this customer". DataAnnotations cannot express these; FluentValidation can, and each validator is a plain class that unit-tests without a web host.

**Why explicit rather than the auto-validation filter.** The ASP.NET auto-validation integration is deprecated, and implicit validation hides *when* checks run. Calling the validator on the first line of the use case means validation happens on the same path whether the caller is a controller, a SignalR hub, or a test.

---

### ADR-09 — LINQ projections and Mapperly, not AutoMapper

**Decision.** Reads project directly into DTOs inside the query. Where an entity-to-DTO mapping genuinely repeats, use Mapperly (MIT, source-generated).

**Why not AutoMapper — licensing.** AutoMapper 15.0.0 and later require a paid commercial licence, under the same Lucky Penny model as MediatR. Same conclusion: a real cost for a product we intend to sell, in exchange for something we do not need.

**Why not AutoMapper — design.** Reflection-based mapping fails at runtime rather than compile time, so a renamed property surfaces as a null in production instead of a build error. It also cannot be translated into SQL, so `ProjectTo` aside, it encourages loading whole entities and mapping in memory.

**Why projections first.** `Select` into a DTO becomes a SQL projection that fetches exactly the needed columns. On the menu endpoint — the most-hit read in the system — that is the difference between selecting six columns and hydrating full entity graphs.

**Where Mapperly earns its place.** Write paths that map a DTO onto a tracked entity, where projection does not apply. Source-generated, so mismatches are compile errors and the generated code is inspectable.

---

### ADR-10 — JWT access tokens with rotating refresh tokens

**Decision.**
- Access token: JWT, ~15 minutes, claims `sub`, `email`, `role[]`, and `restaurant_id` for staff and owners.
- Refresh token: opaque random value, **stored hashed** in `RefreshToken`, long-lived, single-use.
- **Rotation with reuse detection:** each refresh issues a new token and marks the old one used. Presenting an already-used token means it was stolen — the entire token family for that user is revoked immediately.
- Passwords hashed with ASP.NET Core Identity's `IPasswordHasher<T>` (PBKDF2, iteration count and salting handled), without adopting the full Identity framework.

**Why JWT rather than cookie sessions.** Cookie auth is the better default for a browser-only app — it is harder to misuse and gives server-side revocation for free. It is the wrong fit here because React Native is a first-class client, where cookie handling is awkward and inconsistent across platforms. One token scheme across all three clients is worth more than the cookie's advantages.

**Why rotation with reuse detection.** A plain long-lived refresh token, if stolen, works until it expires and nothing reveals the theft. Rotation makes the theft *visible*: the legitimate client's next refresh presents a used token and trips the alarm. For a product handling real orders and real money this is the baseline, not a nice-to-have.

**Why not full ASP.NET Core Identity.** It brings its own user, role, and claim schema, which fights the `User` / `UserRole` / `RestaurantStaff` model in the spec — particularly staff membership scoped to a restaurant. We take the one genuinely hard piece, the password hasher, and leave the rest.

**Consequence of the login decision.** Self-service password recovery requires transactional email, so `IEmailSender` is in the Application layer from day one, with Mailpit in Docker for development. This changes requirement 8.3 — see §5.

---

### ADR-11 — Explicit transition table plus optimistic concurrency

**Decision.** Legal transitions are declared as data in `Domain`:

```
private static readonly HashSet<(OrderStatus From, OrderStatus To)> Allowed = [ ... ];
```

A domain service validates the transition, applies it, and appends the `OrderEvent` in one transaction. `Order` carries a `rowversion` concurrency token.

**Why a table.** It is enumerable. The test can iterate the full cross-product of statuses and assert that exactly the intended pairs are permitted and every other pair throws — which is what "every legal and illegal transition" in the spec actually requires, and it stays true when a status is added later. A `switch` statement cannot be enumerated, so its tests can only cover the cases someone remembered.

**Why not the Stateless library.** A capable state machine library, and a reasonable pick for complex workflows with guards, triggers, and hierarchical states. Ours is seven states and a fixed edge list. The dependency would not remove the transition table, only wrap it.

**Why rowversion.** Two staff on two tablets both press Accept. Without a concurrency token, both succeed, and two `OrderEvent` rows claim the same transition. With one, the second write fails and the UI refreshes. This is the kind of bug that never appears in a demo and appears constantly in a real kitchen.

---

### ADR-12 — SignalR with server-derived groups

**Decision.** Two events, exactly as the spec defines. Group membership is computed on connect from the JWT — `restaurant:{id}` from the `restaurant_id` claim, `user:{id}` from `sub`. No client-supplied group parameter is ever honoured.

**Why.** If the client names its own group, anyone with a valid token subscribes to any restaurant's live order feed. Deriving it from the token makes that impossible by construction rather than by check.

**Scale-out note.** SignalR groups are per-server. The moment this runs on more than one instance, it needs a Redis backplane or clients on different servers miss events. Single instance for now; recorded here so it is a known step, not a production surprise.

---

### ADR-13 — Hosted service for scheduled work

**Decision.** One `BackgroundService` with a `PeriodicTimer` scanning for orders sitting in `Placed` past the threshold and flagging them.

**Why.** It is one job, with no persistence, retry, or scheduling-dashboard requirement. A hosted service is built in, needs no tables, and no extra package.

**Why not Hangfire or Quartz.** Both are good, and either becomes the right answer the moment we need durable retries, a job history, or an operator dashboard. Today they would add a dependency and schema for a single timer.

**Cost accepted.** On multiple instances the scan runs once per instance. Harmless for setting a flag; would need a distributed lock before it does anything with side effects.

---

### ADR-14 — OpenAPI-generated TypeScript client

**Decision.** The API produces an OpenAPI document via the built-in `Microsoft.AspNetCore.OpenApi` support. A generated TypeScript client is committed to the repo and consumed by both Angular apps and the React Native app. CI regenerates and fails if the committed output is stale.

**Why.** Three clients hand-writing the same DTOs is three chances to get a field name wrong, and the failure mode is `undefined` at runtime rather than an error at build. Generated types make a breaking API change a compile error in every client, in the same commit — which is precisely the payoff ADR-01 was chosen for.

**Why committed rather than generated at build.** The diff is reviewable. A pull request that changes the API shows exactly what the clients now see.

---

### ADR-15 — `ProblemDetails` everywhere

**Decision.** All errors return RFC 9457 `ProblemDetails`. Domain exceptions map to status codes in a single `IExceptionHandler`. Validation failures return 400 with per-field detail in a consistent shape.

**Why.** One error contract means one error-handling path per client instead of per-endpoint guesswork, and it is a standard the generated client already understands. Centralising the mapping also stops internal exception text leaking through — the handler decides what is safe to expose.

---

### ADR-16 — Angular CLI workspace, two apps, three libraries

**Decision.**

```
projects/
  storefront/        customer web app
  dashboard/         restaurant staff + platform admin, routes guarded by role
  shared/api-client/ generated client + typed wrappers
  shared/auth/       token storage, refresh interceptor, guards
  shared/ui/         Material theme, shared components
```

**Why one workspace.** The two apps share auth, the API client, and the Material theme. In one workspace those are libraries with real import boundaries, built and tested once. As two standalone projects they become copy-paste, and the copies drift.

**Why the dashboard serves both restaurant staff and platform admin.** They share the shell, the auth flow, and most tables; they differ by route and permission. Two apps would duplicate all of it for a difference a route guard expresses.

**Why the storefront stays separate.** Different audience, different bundle. A customer should not download the admin app's reporting code, and public pages have SEO and performance requirements the dashboard does not.

**Why not Nx.** Better task caching, dependency graphing, and generators — genuinely valuable at ten-plus projects or with several teams. At two apps and three libraries the Angular CLI workspace does the same job with one less tool to configure and keep current. Nx remains a clean migration if the workspace grows.

---

### ADR-17 — Signals and RxJS, no global store

**Decision.** Signals for component and feature state; RxJS for streams — HTTP, SignalR, form and route events. Feature state lives in an injectable service holding signals. No NgRx.

**Why.** Almost all of our state is *server* state, and the cart — the one piece that would normally justify a client store — is server-side by design (spec §6.5). What remains is per-feature and small.

**Why not NgRx.** Its value is a single auditable state tree with time-travel debugging, and it pays off in large apps with complex cross-feature client state. Here it would mean actions, reducers, effects, and selectors wrapped around what is fundamentally "fetch, display, refresh on a SignalR event".

**If caching becomes the problem.** The right answer is a query-caching library, not a global store — the actual need would be request deduplication and invalidation, which is not what NgRx is for.

---

### ADR-18 — Testcontainers, never the InMemory provider

**Decision.**

| Layer | Tool | Covers |
|---|---|---|
| Unit | xUnit, no I/O | Transition table, option min/max, settlement maths, money rounding |
| Integration | xUnit + Testcontainers (real SQL Server) | Tenant isolation, idempotency, concurrency, migrations |
| E2E | Playwright | Order arrives → accept → advance → delivered |

**Why real SQL Server in a container.** Three of our most important properties are *database* properties and cannot be tested anywhere else: the unique index that makes the idempotency key work, the `rowversion` conflict that blocks a double-accept, and `decimal(10,2)` rounding behaviour. Testcontainers starts a real SQL Server per test run, so migrations are exercised on every CI build.

**Why not the EF InMemory provider.** It is not a relational database. It does not enforce unique indexes, foreign keys, or check constraints, and it does not translate the same LINQ. Every one of the three properties above passes there whether the code is correct or not — the tests would be worse than none, because they would report confidence that does not exist. (The EF team's own guidance is not to use it for this.)

**Why not SQLite in-memory.** Closer, but differs on decimal handling, collation, and concurrency tokens — exactly the areas we most need to trust.

**Assertions: Shouldly, not FluentAssertions.** FluentAssertions 8.0 moved to the Xceed Community License in January 2025 and costs $130 per developer per year for commercial use; version 7.x stays Apache 2.0. Shouldly is free, reads about the same, and carries no licence question — the third library excluded on these grounds, after AutoMapper and MediatR.

---

### ADR-19 — Docker Compose for local infrastructure

**Decision.** Compose brings up SQL Server, Mailpit (to inspect password-reset email without sending it), and later the API itself. Seed data runs from a CLI flag, not automatically on startup.

**Why.** One command to a working environment, identical across machines, and no local SQL Server install to conflict with anything else. Seeding stays explicit because a seeder that runs on startup eventually runs somewhere it should not.

---

## 4. Resulting structure

```
Ordering_System/
├── docs/
│   ├── ARCHITECTURE.md
│   ├── PROJECT-SPEC.md
│   └── REQUIREMENTS.md
├── api/
│   ├── OrderingSystem.sln
│   ├── src/
│   │   ├── OrderingSystem.Domain/
│   │   │   ├── Entities/
│   │   │   ├── Enums/
│   │   │   ├── Orders/            OrderStateMachine, transition table
│   │   │   ├── Money/             rounding, LBP conversion
│   │   │   └── Exceptions/
│   │   ├── OrderingSystem.Application/
│   │   │   ├── Abstractions/      IAppDbContext, IEmailSender, IPaymentGateway,
│   │   │   │                      ITenantContext, IClock
│   │   │   ├── Common/
│   │   │   └── Features/          Auth, Restaurants, Menu, Options, Cart,
│   │   │                          Orders, Addresses, Zones, Settlement, Reports
│   │   ├── OrderingSystem.Infrastructure/
│   │   │   ├── Persistence/       AppDbContext, Configurations/, Migrations/, Seed/
│   │   │   ├── Identity/          password hashing, token service
│   │   │   ├── Email/
│   │   │   └── Payments/
│   │   └── OrderingSystem.Api/
│   │       ├── Controllers/
│   │       ├── Hubs/
│   │       ├── Middleware/
│   │       └── Program.cs
│   └── tests/
│       ├── OrderingSystem.Domain.Tests/
│       ├── OrderingSystem.Application.Tests/
│       └── OrderingSystem.Api.IntegrationTests/
├── web/
│   ├── angular.json
│   ├── projects/
│   │   ├── storefront/
│   │   ├── dashboard/
│   │   └── shared/{api-client,auth,ui}/
│   └── e2e/
├── mobile/
├── docker/
│   └── docker-compose.yml
└── .github/workflows/
```

Each `Features/<Name>` folder holds its use cases, DTOs, and validators together — the readability of vertical slices, inside boundaries that still enforce the dependency rule.

---

## 5. Changes to the spec from this round

| Spec reference | Was | Now | Effect |
|---|---|---|---|
| §7.5, req 6.7 | Tax applied, single configurable rate | **No tax** | `Order.TaxUsd` stays on the table, always `0`. Total = subtotal + delivery fee − discount |
| Req 8.3 | "No email or SMS" | **Transactional email in; SMS still out** | Required by self-service password recovery. Marketing email remains out of scope |
| §11 | `register`, `login` only | **+ `refresh`, `logout`, `forgot-password`, `reset-password`** | Follows from the login decision |
| §6.1 | — | **+ `RefreshToken`, `PasswordResetToken`** | Follows from ADR-10 |
| §6 | No settings table | **+ `PlatformSetting`** | Holds default commission and the stale-order threshold |
| §12 | .NET 8 | **.NET 10** | ADR-05 |
| Q13 | — | **English only** | No i18n scaffolding. CSS still uses logical properties, which costs nothing and keeps the door open |

**On keeping `TaxUsd` at zero.** Removing the column saves nothing today and costs a migration, a backfill, and a recomputation of historical order totals if Lebanese VAT ever has to be charged. A column that is always zero is the cheapest possible option on a future requirement.

**On the commercial framing.** Three items the spec cut as "portfolio-acceptable" deserve revisiting before this goes in front of a paying customer, though none blocks Phase 1: the mock payment gateway will need a real Lebanese processor, deployment target is still undecided, and the out-of-scope list (ratings, promo codes, push notifications) was scoped for a demo rather than a product.

---

## 6. Still open

| # | Question | Blocks |
|---|---|---|
| 1 | FriesLab item-modal screenshots — for realistic seed data, and to confirm no conditional option groups | Phase 2 seed, not the schema |
| 2 | Per-item min/max override on `MenuItemOptionGroup` — include the nullable columns now? *(recommended: yes)* | Phase 1 schema |
| 3 | Default commission percentage | Phase 6 |
| 4 | Rejection reason list contents | Phase 3 |
| 5 | Deadline — decides Phases 1–4 versus all 7 | Planning |
| 6 | Deployment target: VPS with Docker, or a managed cloud host | Phase 7 |
