# Phase 2 — Menu

**Goal:** a restaurant can sign in and edit its own menu, and anyone can browse menus.
**Status:** planning
**Builds on:** Phase 1 (database, auth, tenant isolation, CI — 64 tests)

---

## 1. What "done" looks like

Nadia signs in at FriesLab, adds a burger, gives it a price, attaches the Extras group, uploads a
photo, and marks it unavailable when the kitchen runs out. A customer opening the storefront sees
her menu, and does not see the item she hid.

Two things must be true underneath: she cannot touch Beirut Mezze House's menu even by typing its
id into the URL, and the menu she edits is the same data the ordering flow will read in Phase 3.

## 2. Where this differs from Phase 1

Phase 1 was one codebase. Phase 2 is two, and the second one is visible — which changes how it
gets reviewed. Screenshots are how you check a screen, not a diff.

It is also **bigger than Phase 1**: the backend half is roughly a third of the work, and the
Angular half is the rest, including the entire client-side auth story that has no equivalent
yet.

---

## 3. Steps

### Step 1 — Public menu endpoints

```
GET /api/restaurants?zoneId=&page=      list, filtered by delivery zone
GET /api/restaurants/{slug}             one restaurant with hours and zones
GET /api/restaurants/{slug}/menu        categories and items, one round trip
GET /api/menu-items/{id}                one item with its option groups
```

Anonymous. These are the marketplace's front door, so they are also the first endpoints anyone
can hit without a token.

Two things worth care:

- **The menu endpoint is the most-requested read in the system.** It projects straight into DTOs
  rather than loading entities — the difference between selecting six columns and hydrating a
  graph, on the query that runs most often.
- **`EffectiveMinSelect` / `EffectiveMaxSelect` must be resolved server-side.** The client must
  never be handed a group cap and an override and asked to work out which applies.

### Step 2 — Staff menu endpoints

```
CRUD /api/restaurant/categories
CRUD /api/restaurant/menu-items
CRUD /api/restaurant/option-groups
CRUD /api/restaurant/options
PATCH /api/restaurant/menu-items/{id}/availability
```

**The first real users of `ITenantGuard`.** It has been written and unit-tested since step 8 with
no production caller; this is where it starts earning its place. Every write loads the row, checks
ownership, and only then changes anything.

Deletes are soft, because order lines point at these rows.

### Step 3 — Image upload

Needs a decision — see §4.

```
POST   /api/restaurant/menu-items/{id}/image
DELETE /api/restaurant/menu-items/{id}/image
```

Validated on content, not on file extension: a `.png` that is actually a script is the oldest
upload bug there is. Size capped, dimensions capped, re-encoded rather than stored as received.

### Step 4 — Angular workspace

```
web/
  projects/
    dashboard/          restaurant staff + platform admin   ← built this phase
    storefront/         customer web                        ← scaffolded, built in Phase 5
    shared/api-client/  generated
    shared/auth/        token storage, interceptor, guards
    shared/ui/          Material theme, shared components
```

Angular 22.1.6, pinned. Both apps are scaffolded now so the shared libraries have two consumers
from the start — a library with one consumer tends to grow that consumer's assumptions.

### Step 5 — Generated API client

The OpenAPI document already lists every endpoint and is already tested for it. This turns it into
TypeScript, committed to the repo so the diff is reviewable, with CI failing if it goes stale.

ADR-14's payoff arrives here: a breaking API change becomes a compile error in the Angular app, in
the same commit, rather than a runtime `undefined` found by a user.

### Step 6 — Auth in the browser

The half of Phase 1's auth that does not exist yet:

- Login page, and a forgot/reset password pair
- Access token in memory, refresh token in `localStorage` — not the other way round
- An interceptor that catches 401, refreshes once, retries, and **queues concurrent requests**
  behind that single refresh. Without the queue, three parallel 401s trigger three refreshes, two
  of them replay a spent token, and Phase 1's theft detection logs the user out for doing nothing
  wrong
- Route guards by role

That interceptor is the single most delicate piece of Phase 2, and it is delicate precisely
because the backend is strict.

### Step 7 — Dashboard shell

Layout, navigation, the Material theme, role-based routes. Small, but everything after it hangs
here.

### Step 8 — Menu editor

The actual feature. Category list with reordering, item form with price and description, option
group editor with min/max, per-item override, availability toggles, image upload.

The option group editor is the interesting screen: it has to make "required, pick one" and "choose
up to 3" understandable without exposing two integers to a restaurant owner.

### Step 9 — E2E and frontend CI

Playwright: sign in, add an item, see it on the public menu. Frontend build and tests added to CI,
path-scoped so an API change does not rebuild Angular and vice versa.

---

## 4. Decisions needed before step 3

| # | Question | Recommendation |
|---|---|---|
| 1 | **Where do uploaded images go?** Local disk, or object storage (MinIO/Azurite in dev, S3/Azure Blob in production)? | Behind an `IFileStorage` interface, local disk in development. Local disk alone does not survive a container restart and does not scale past one server, so production needs object storage — the interface is what makes that a swap rather than a rewrite |
| 2 | **Which client generator?** NSwag, Kiota, `openapi-typescript`, `@hey-api/openapi-ts` | **NSwag.** It emits Angular services using `HttpClient` and returns Observables, which matches ADR-17 rather than fighting it. The others produce fetch-based clients that would need wrapping |
| 3 | **Paginate the restaurant list?** | Yes for restaurants, no for a single menu. A menu is tens of items and wants one round trip; a marketplace's restaurant list is unbounded |
| 4 | **Does the storefront get built this phase?** | No — scaffolded only. The spec puts customer clients in Phase 5, and the dashboard is what proves the menu API works |

---

## 5. How this gets reviewed

Backend steps read like Phase 1: a diff, a test run, a commit.

Frontend steps get **screenshots**. I can run the dev server and render pages here, so each screen
arrives as an image alongside the code rather than as a description of an image.

Your own machine still matters for anything interactive — clicking through a form, checking a
transition feels right, trying it on a phone. But "does this screen look correct" no longer has to
wait for you.

---

## 6. Sequence

Backend before frontend, because the generated client needs endpoints to generate from:

```
1 → 2 → 3        backend menu API
        4 → 5    workspace, then the client it generates
            6    auth, before any screen that needs a token
            7 → 8   shell, then the editor
                9   E2E and CI
```

Steps 1 and 2 can start immediately. Step 3 waits on decision 1.
