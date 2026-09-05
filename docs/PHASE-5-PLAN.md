# Phase 5 — The storefront

Phase 2 gave a restaurant a menu, Phase 3 let it sell from that menu, and Phase 4 let a restaurant
be set up and run without anybody touching the database. Everything a customer does still goes
through curl.

This phase is the shop window: the application a person actually orders dinner from.

---

## 1. What "done" looks like

Somebody who has never seen this product opens it on a phone, finds a restaurant that delivers to
where they live, reads its menu, builds a basket with the choices a dish demands, signs up, says
where it is going, sees what it will cost and how long it will take, orders it, and watches it
being cooked — and none of that touches a screen a restaurant uses.

The measure is the same one Phase 4 used, from the other side: **a customer can be onboarded**.

---

## 2. What is already there, and what is not

Most of the API this phase needs was built in Phase 3 and has been exercised by integration tests
and by `web/e2e/helpers.ts` ever since — but never by a person.

| Already built | Where |
|---|---|
| Browse restaurants, read a menu, read one dish and its option groups | `api/restaurants`, `api/menu-items` |
| A basket per customer per restaurant, priced by the server | `api/restaurants/{id}/cart` |
| A quote: subtotal, delivery fee, promise, the total in dollars and lira | `.../cart/quote` |
| Checkout, with every refusal already worded for a person | `.../orders` |
| A customer's own orders, and cancelling one while it is allowed | `api/orders` |
| Live order updates, already grouped per customer | `/hubs/orders` |
| Register, sign in, refresh, forgotten password | `api/auth` |
| Session, token refresh, guards, interceptor — app-agnostic | `web/projects/shared/auth` |

| Missing | Why it matters |
|---|---|
| **Addresses.** No endpoint exists at all; they are seed data. | Nothing about delivery works without one. Phase 4's onboarding test could not follow a delivery fee to a customer's bill for exactly this reason, and had to place a pickup order instead. |
| **A profile.** No way to change your own name, phone or password once signed in. | A phone number that cannot be corrected is a courier ringing the wrong number. |
| **The storefront application.** `projects/storefront` is the CLI's scaffold: one component, `routes = []`. | It is the whole phase. |

`web/projects/shared/ui` is also still the scaffold — a `lib-ui` component that renders "ui works!".
Two applications now want the same order-status wording, the same money formatting and the same
empty-state shape, so this is where that library earns its name or gets deleted.

---

## 3. Where this differs from every phase so far

**The reader is not a professional.** A restaurant owner will learn a screen they use every day; a
customer will not learn anything. Every refusal has to be a sentence, every price has to be
obvious before it is charged, and nothing may need a second attempt to understand.

**It is a phone.** The dashboard is a laptop on a counter. This is one hand, in a queue, on a bad
connection. Layout starts narrow and grows, not the other way round.

**Lira matter.** The quote already returns a total in both currencies. A price shown only in
dollars is not a price most people here can act on.

**Almost none of it is new API.** Four of the seven steps below are screens over endpoints that
already exist and are already tested. That is the reward for the order of work — but it also means
the risk in this phase is the interface, not the server, and that is where the effort goes.

---

## 4. Steps

### Step 1 — The shell, and finding somewhere to eat ✅

The storefront application proper: layout, routing, and the two screens that need no account —
the list of restaurants, and one restaurant's page with its menu.

A restaurant's card has to say whether it is **open now**, and if not, when it opens. All three of
the states Phase 4 built are visible from here: shut by the platform (not listed at all), outside
its opening hours, or paused by the kitchen — and the last two read differently to somebody
deciding where to order from.

Filtering by "delivers to me" needs an address, which arrives in Step 3. Until then the list shows
everything and each restaurant's page says where it delivers.

**Two contract records were both called `OpeningWindow`.** Different C# namespaces, one flat
OpenAPI schema namespace — so they collapsed into one schema, the hours editor's shape won, and
the generated client had been describing a restaurant's public hours with the wrong field name
since Phase 4. The contract-drift job in CI structurally cannot see this: it regenerates from the
same wrong document and finds no difference. `ContractNameCollisionTests` now walks the
Application assembly for duplicate record names under `Features.` and fails on one.

### Step 2 — Being somebody ✅

Register, sign in, sign out, forgotten password, and a profile that can correct a name, a phone
number or a password.

The machinery all exists in `shared/auth`; this is screens plus one small endpoint for the profile.
The invitation flow Phase 4 built lands here too: somebody invited to run a restaurant follows a
link into `/reset-password`.

**`/reset-password` did not exist in either application.** Every password-reset email since Phase 1
and every staff invitation since Phase 4 pointed at a route neither app routed. The dashboard's
catch-all sent it to `''`, whose `authGuard` bounced to `/login` and dropped the token on the way,
so the link could not have worked for anybody. Nothing caught it because every test followed those
links through the API rather than through a browser — the screen at the end of the link was the
one part nobody had ever opened. It is now one component in `shared/ui`, routed by both apps, and
verified by driving a real emailed link in a real browser in each.

**One email, two destinations.** A customer who forgot a password belongs on the storefront; an
invited owner belongs on the dashboard, because that is where the screens they were invited for
are. `AuthOptions` grew `DashboardBaseUrl` beside `AppBaseUrl`, and two integration tests pin which
origin each email carries so the pair cannot quietly become one again.

**The password rules had already drifted.** The server has asked for ten characters with a letter
and a digit since Phase 1. All three front-end forms asked for eight and checked nothing else, so
each would accept a password, send it, and show a server validation message in place of its own.
The rules now live once in `shared/ui` — which is what that library was for — and a test pins the
numbers against the C# validator, since FluentValidation's rules are not in the OpenAPI document
and no generated client can carry them.

**The mismatch message could not be seen.** Comparing the two password boxes in a component method
left the confirmation control valid, and Angular Material only draws a `mat-error` for a control in
an error state — so pressing the button did nothing and said nothing. The check is a group
validator that sets the error on the control the message sits under. A unit test found it; a person
would have found it as a form that ignores them.

**`[AllowAnonymous]` on the controller silently overrides `[Authorize]` on an action.** The
analyser (ASP0026) refused to build `/me` endpoints added to `AuthController`, which is the only
reason they are not anonymous today. They live in their own `MeController`.

**The toolbar account menu was measured and removed.** `mat-menu` pulls the CDK overlay into the
initial bundle: 17kB gzipped on every first load, paid by every visitor including the majority who
never sign in, to save one tap for the ones who do. The avatar is a link to the account page, which
already shows the address and now carries the sign-out button.

### Step 3 — Where the food is going

The one real gap: `GET/POST/PUT/DELETE /api/addresses`, and the screen for them.

An address is a delivery zone plus the words that get a courier to a door — building, floor,
landmark. The zone is not decoration: it is what decides whether a restaurant serves you at all
and what the delivery costs, so it is picked from the platform's list rather than typed.

One address is the default. A customer with three saved addresses ordering at speed should not
have to choose every time, and should not be able to send dinner to the wrong one by accident.

### Step 4 — The basket

A dish, its option groups, and the rules they carry — required, pick one, pick up to three, this
one costs extra. Phase 2 built those rules and Phase 3 prices them; this is the hardest interface
in the phase and the one most likely to be got wrong.

The running total comes from the server's quote, never from arithmetic in the browser. Two places
computing a price is how they come to disagree, and the one the customer sees would be the wrong
one.

### Step 5 — Checkout

Pickup or delivery, which address, cash on delivery, and the promise. Then the refusals, each of
which already has a sentence waiting for it: the kitchen is closed, the kitchen is paused, the
basket is under the minimum, a dish sold out while you were deciding, the restaurant does not
deliver where you are.

Those refusals are the point of this step. Any of them arriving as "Something went wrong" would
undo the care Phase 3 took in wording them.

### Step 6 — Watching it happen

The screen somebody stares at while they wait: where the order is, what was promised, and the
restaurant's phone number. Live, over the hub group Phase 3 built for exactly this and which no
customer has ever connected to.

Cancelling belongs here, offered only while the state machine allows it — and the trail of what
happened when, which the order detail endpoint already returns.

Order history, and reordering from it if it comes cheap.

### Step 7 — E2E and phase close

A customer from never having visited to a delivered order, in a browser, on a phone-sized viewport:
find a restaurant, sign up, save an address, build a basket with required choices, check out with
delivery, watch the kitchen work it, and see the delivery fee the owner set in Phase 4 on the bill.

That last clause is what closes Phase 4's one open loop.

---

## 5. Decisions, with my recommendations

Taken rather than asked, unless the answer changes what gets built.

| # | Question | Recommendation |
|---|---|---|
| 1 | **Guest checkout, or an account first?** | **An account.** An order belongs to a customer: the query filter, the order history, the live updates and cancelling are all keyed on a user id. Guest checkout is a second identity concept threaded through all of it, for a convenience that a fast sign-up mostly removes |
| 2 | **Sign up with email or phone?** | **Email, as today.** Phone-first is the Lebanese norm and worth revisiting, but it needs SMS delivery, a verification flow and a different unique key on `Users` — a phase's worth of work hiding inside a checkbox |
| 3 | **Search, or a list with filters?** | **Filters.** Open now, and delivers to my address. There are three restaurants; text search over three names is a box that makes the product look emptier than it is. Search arrives when the catalogue does |
| 4 | **One basket, or one per restaurant?** | **One per restaurant** — which is what `Cart` already is, keyed on (user, restaurant). The screen's job is to make it obvious which basket is open, because the model already allows several |
| 5 | **Does the storefront use the live hub?** | **Yes.** The customer group was built in Phase 3 and has never had a client. An order that only updates when you pull to refresh is the thing every food app is judged on |
| 6 | **Show lira?** | **Yes, alongside dollars.** The quote already returns both, frozen at the rate the order was placed at. Showing only dollars would make the price something most people have to convert in their head |
| 7 | **Online payments?** | **Still not here.** Cash on delivery, as Phase 4 decided. Payments remain their own phase, and nothing in this one is blocked by them |
| 8 | **What happens to `shared/ui`?** | **It earns its name or it goes.** Two applications now want the same status wording, money formatting and empty states. If, by Step 6, nothing has moved into it, delete it rather than leave a scaffold called `ui` in a repository that otherwise means what it says |

---

## 6. Sequence

Step 1 comes first: there is nothing to look at until there is an application. Step 2 unblocks
anything that needs an account. **Step 3 blocks Step 5** — delivery checkout cannot be built or
tested without an address, which is the lesson Phase 4 ended on. Step 4 needs Step 1's menu.
Step 6 needs an order to watch, so it follows Step 5. Step 7 needs all of them.

The only piece with real API work in it is Step 3, and it is early on purpose.
