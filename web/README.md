# Web

The Angular workspace: two applications and four shared libraries.

| Project      | Type        | What it is                                                        |
| ------------ | ----------- | ----------------------------------------------------------------- |
| `dashboard`  | application | Restaurant staff — the queue, history, menu and hours.            |
| `storefront` | application | The customer-facing ordering site.                                |
| `api-client` | library     | **Generated.** TypeScript client for the API. Never edit by hand. |
| `auth`       | library     | Tokens, refresh, route guards, HTTP interceptor.                  |
| `realtime`   | library     | The live channel to the orders hub, and the poll behind it.       |
| `ui`         | library     | Presentational pieces both applications share.                    |

## Prerequisites

Node is pinned in [`.nvmrc`](.nvmrc); Angular refuses to run on an older one.

```bash
nvm use          # reads .nvmrc
npm ci           # installs exactly what package-lock.json says
```

## Running

```bash
npm start                    # dashboard on http://localhost:4200
npx ng serve storefront      # storefront on http://localhost:4201
```

Both proxy `/api`, `/media` and `/hubs` to the backend on `http://localhost:5080`
([`proxy.conf.json`](proxy.conf.json)), so run the API first — see the root
[README](../README.md). The `/hubs` entry carries `ws: true`: without it the dev server proxies
SignalR's negotiate but not the WebSocket that follows, and the connection quietly falls back to
long polling in development only — which works, and hides a problem until production.

## Testing

```bash
npx ng test dashboard --watch=false
npx ng test auth --watch=false
npx ng test realtime --watch=false
```

CI runs the suite for every project that has one. If you add specs to a project, add it to the
`Test` step in [`ci.yml`](../.github/workflows/ci.yml) — a project that is never named is a
project whose tests never run.

## End-to-end tests

A real browser against the real API and a real database. These are the only tests that prove the
pieces are joined — that a dish typed into the dashboard reaches a table and comes back on the
public menu.

They need the API running first, because Playwright can wait for a port but not for "migrations
applied and seeded":

```bash
# once, from the repository root
dotnet run --project api/src/OrderingSystem.Api -- --seed
dotnet run --project api/src/OrderingSystem.Api --urls http://localhost:5080

# then, from web/
npm run e2e          # headless
npm run e2e:ui       # the Playwright UI, for writing them
```

Playwright starts the Angular dev server itself and reuses one already running.

**On a machine that already has Chromium** — a dev container, usually — Playwright may want a
different revision than the one installed. Point it at what is there instead of downloading:

```bash
export PLAYWRIGHT_CHROMIUM_PATH=/opt/pw-browsers/chromium
```

CI leaves that unset and installs its own, so the pipeline is not tied to any one machine.

A note on the data: these tests share one database and clean up after themselves, except for
option groups, which have no delete endpoint. CI starts from an empty database every run, so
this only shows up locally as a few leftover `E2E Group …` rows.

## Regenerating the API client

`api-client` is generated from the API's OpenAPI document. After changing a controller or a DTO:

```bash
./scripts/generate-api-client.sh      # or .ps1 on Windows
```

Commit the result. It is checked in deliberately, so a fresh clone builds without a running API,
and so a diff in the client shows up in review as the API contract change it is.

## Why the libraries are not built as packages

`ng build dashboard` works. `ng build auth` does not, and that is expected.

The root `tsconfig.json` maps `api-client`, `auth` and `ui` to their **source** files. That is
what makes editing a library show up instantly in a running app, with no build step and no
stale `dist` to explain. The cost is that `ng build <library>` — which packages a library for
npm — cannot run, because ng-packagr requires every file to sit inside the library's own
`rootDir`, and `auth` imports `api-client` source that sits outside it.

Nothing is lost by that. These libraries are internal to this workspace and are not published,
and an application build compiles their sources as part of its own program, so a type error in
`auth` fails `ng build dashboard`. CI therefore builds the two applications and tests every
project that has specs.

If a library ever does need publishing, point the `paths` entries at `dist/` and build in
dependency order — a deliberate trade of developer experience for a shippable package.

## Fonts

Roboto and the Material Icons font are bundled from `node_modules` in `styles.scss`, not fetched
from Google's CDN: the product is used on Lebanese connections that are often slow or down, a
cross-origin round trip before first paint is the slowest thing on a cold load, and no visitor's
IP needs to reach a third party for the page to render. Latin subsets only — the product ships
in English.
