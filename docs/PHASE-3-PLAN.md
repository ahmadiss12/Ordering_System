# Phase 3 — Orders

Phase 2 gave a restaurant a menu it can edit. Phase 3 makes that menu earn money: a customer
builds a cart, places an order, and the kitchen works it through to delivered.

This is the phase where the product becomes a product. Everything before it was setup.

---

## 1. What "done" looks like

A customer picks dishes from FriesLab, chooses their options, sees a total they can trust, and
places an order. It appears on the kitchen screen **without anyone pressing refresh**. Staff
accept it, cook it, send it out, mark it delivered — and every one of those steps is recorded
with who did it and when.

An order that should not be possible, is not: not below the minimum, not to a zone the
restaurant does not serve, not while it is closed, not containing a dish that sold out while the
customer was deciding.

---

## 2. Where this differs from Phase 2

Phase 2 was CRUD. A menu item is a row; editing it changes the row; the worst outcome of a bug
is a wrong price on a screen.

Phase 3 is not CRUD, and three things make it harder:

**Money.** A total the customer agreed to must never be recomputed later into a different
number. Every price, fee and name is copied onto the order at the moment it is placed — the
tables were built for that in Phase 1, and this phase is where the copying actually happens.

**Time.** Two people can order the last portion at once. A restaurant can close between the
cart and the checkout. A price can change while a customer reads the menu. None of these are
edge cases; all of them happen on a Friday night.

**State.** An order moves through statuses, and most moves are illegal. Delivered cannot go back
to preparing. A pickup order never goes out for delivery. Rejecting after accepting is not a
rejection, it is a cancellation, and it means something different in a report.

---

## 3. Steps

### Step 1 — The order state machine ✅

Pure domain, no database. One table of legal transitions, and the rules that guard them:
who may make each move, which are terminal, which require a reason, which depend on whether the
order is delivery or pickup.

`OrderStatus` already carries a comment promising this exists in one place. It does not yet.
Everything after this step depends on it, so it goes first and gets tested hardest — this is the
file where a missing row means a delivered order can be cancelled.

**Decided during the step.** A restaurant that accepts and then cannot fulfil — a power cut, an
ingredient gone — needed a way out; without one the order sits in `Preparing` forever, which is
worse for everybody than a cancellation somebody can see. So a restaurant may cancel from
`Accepted` and `Preparing`, and it must give a reason, because that is what the rejection-rate
report counts.

That made the reason rule depend on **who** is moving rather than only on where to. A restaurant
dropping an order is reportable; a customer changing their mind is nobody's business, and a form
between them and the button would be rude.

It also improved a message. A customer asking to cancel a `Preparing` order now gets "this order
is already being prepared … call the restaurant" as a conflict, rather than a 403 saying the move
belongs to somebody else — true, but useless, since they could have done it a minute earlier.

### Step 2 — Cart ✅

Server-side, keyed by customer and restaurant, because the tables were designed that way and a
cart that lives in one browser is lost the moment someone switches to their phone.

Add a line with its chosen options, change a quantity, remove it, empty the cart. The **selection
rules from Phase 2 are enforced here**: a group that says "choose 1" rejects a line with two.

**Two bugs found by the tests, both real.** A new line added to an *existing* cart was silently
marked as an update rather than an insert — EF decides the state of an entity it meets through a
navigation by whether the key is already set, and a Guid key we generate ourselves looks like a
row that already exists. It only worked while the cart itself was new, because children of a new
parent are new too. And emptying a cart came back reporting the lines it had just removed,
because the response was built from the tracked graph rather than from the database.

### Step 3 — Pricing ✅

One place that computes a total, on the server, and a quote endpoint so the client can show the
same number before committing. The client never sends a price.

Subtotal from lines and option deltas, delivery fee from the zone, commission for the platform's
books, promised time from the restaurant's prep minutes. Tax stays zero — the column exists for
the day Lebanese VAT applies.

**Rules decided here, each one a business choice rather than arithmetic:**

| Rule | Choice | Why |
|---|---|---|
| Commission base | **Food only**, not the delivery fee | Charging commission on the fee would bill the restaurant for the courier |
| Rounding | Two places, **halves away from zero** | Banker's rounding is right for statistics and wrong on a receipt a customer adds up by hand |
| Minimum order | Measured against the **subtotal** | A delivery fee carrying somebody over the minimum would make the minimum meaningless |
| Promised time | Prep + travel, then a **10-minute window** | A promise to the minute is one no kitchen keeps and every customer judges it by |
| Lebanese pounds | **Whole**, at the rate in force; null when no rate is set | No smaller unit is in circulation, and an invented rate is worse than no figure |

A below-minimum basket still gets a quote, with the shortfall named. Refusing to price it would
leave the screen unable to say how much more is needed.

### Step 4 — Checkout ✅

The step that turns a cart into an order, and the one with the most ways to fail:

- minimum order not met
- restaurant closed, or not accepting orders
- delivery address outside every zone the restaurant serves
- an item unavailable or deleted since it went into the cart
- a price changed since the cart was built

Each is a specific, readable refusal, not a generic 400. On success: allocate the daily order
number, snapshot every name and price, write the first `OrderEvent`, empty the cart — in one
transaction.

**A Phase 1 feature nearly went unused.** `Order.IdempotencyKey` carries a unique index and a
comment explaining that a double-tap on a poor connection should return the original order rather
than placing a second. Nothing set it, so every order took `Guid.Empty` and the second one ever
placed collided with the first. It is now part of the checkout request, required, and answered
before any other check — by the time a repeat arrives the basket is empty, so asking "is your
basket empty" first would tell a customer their order failed when it had succeeded.

**Order numbers** read as `FRIESLAB-260902-042`: restaurant, day, and the count a kitchen shouts
across a counter. Allocation is one `MERGE ... OUTPUT` with `HOLDLOCK`, because reading a counter
and writing it back as two statements is how two customers on a Friday night get the same number.

### Step 5 — Order endpoints ✅

Reading and moving orders, with the tenant guard on every one.

Customer: place, list mine, one order in detail, cancel while it is still cancellable.
Restaurant: the queue, accept, reject with a reason, advance status.

Split in two, because reading and moving are different jobs and each deserves its own review.

**5a — reading ✅.** A customer's history, a kitchen's queue filtered by status, and one order in
full with its lines, its options, its event trail and the delivery address as recorded at the
time. The detail also returns the moves this caller could make next, straight from the transition
table, so a screen never draws a button that would be refused. Nothing restates the tenant rule:
the query filter on `Order` has already decided what the caller may see, and repeating it would
be a second place for it to be wrong.

**A test-design fault fixed here.** Every test that placed an order failed for the ten hours a day
FriesLab is shut — the checkout behaving correctly and the tests being wrong to depend on the hour
they ran at. The integration tests now run on a clock whose local time is pinned to one in the
afternoon and can be moved, which is what `IClock` was built for. That also made two tests
possible that could not be written before: a closed restaurant refusing an order, and an order
placed at one in the morning still landing inside FriesLab's overnight window.

Its UTC time deliberately stays real. Tokens are validated by the framework's own clock, which no
test can move, so a UTC time hours away from the real one would make every request arrive expired.

**5b — moving ✅.** Accept, reject, advance, cancel — each one through the state machine, each
one appending an `OrderEvent` that records who did it, and both written in a single transaction so
an order can never carry a status no entry explains.

**One endpoint, not four.** `POST /api/orders/{id}/status` takes the status to move to, rather than
offering `accept`, `reject`, `advance` and `cancel`. The transition table already decides what may
follow what and who may do it; four named endpoints would be a second copy of that table, extended
by hand every time a status is added. The detail endpoint hands a screen the moves it may make and
the screen posts one of them straight back.

**The route does not decide who is asking.** Customer and kitchen use the same endpoint, and which
party they are is worked out from the order. That matters for the person who is both — a cook
ordering their own lunch — where the *move* decides the hat: accepting is the restaurant's, and
cancelling a placed order is the customer's, and one person may make either. Putting the party in
the route would have made them pick a hat before pressing a button.

**A restaurant's cancellation carries the same reason a rejection does**, in the same column. The
rejection-rate report then asks one question — which orders carry a reason — and finds every order
the restaurant dropped, whichever way it dropped it. A customer changing their mind sets nothing,
which is what keeps them out of that report.

**A reason on a move that does not take one is refused rather than dropped.** Accepting an order is
not a refusal, and a reason recorded there would quietly make that report wrong. Silently discarding
it would leave a client believing it had been kept.

**Two tablets, one order.** The rowversion means the second write fails instead of both appearing to
succeed, and the loser is told where the order actually is now rather than being handed a 500.
Deleting that handling makes the concurrency test fail every run, so the race is genuinely happening
rather than being serialised away by the database.

**A bug from step 4, found by a sweep rather than by a failure.** Comparing every validator against
its column turned up one that disagreed: a customer note was allowed 1000 characters and the column
held 500, so a long note passed validation and then died in SQL Server on a truncation error — which
reaches the customer as a 500 saying nothing, with their order not placed. Both are 500 now, and a
test sends 501 characters and insists on a 400.

**A limitation worth naming, not fixed here.** The query filter gives staff their restaurant's
orders *instead of* their own, so a restaurant owner who orders from a different restaurant cannot
see or cancel that order. It is the security boundary ADR-07 is built on, and widening it deserves
its own review rather than a change made in passing during a step about something else.

### Step 6 — Live updates ✅

A kitchen screen that misses an order is worse than no screen. SignalR, scoped by the same
`restaurant_id` claim the query filters use, with polling as the fallback when a connection
drops — a phone on Lebanese mobile data will drop.

Split in two: the channel, then the screen's end of it.

**6a — the hub ✅.** A hub at `/hubs/orders` that pushes an order change to the kitchen watching it
and the customer waiting on it.

**It has no methods a client can call.** That is the design, not an omission. A hub method taking a
group name would let any connection ask to join `restaurant:{somebody else}` — the entire isolation
model undone by one string parameter. Membership is decided on connect, from claims on a token this
server signed, and a client's only power is to connect or not.

**One group per connection, and the either/or is the query filter's.** A caller with a restaurant
claim sees that restaurant's orders *instead of* their own, so the restaurant group is already
everything they are entitled to hear. **This was found by a failing test, not by design:**
`Clients.Groups` walks each group in turn without tracking connections it has already reached, so a
cook ordering their own lunch — in the restaurant group and their own customer group — had the same
order pushed to their screen twice. The comment claiming SignalR deduplicated was simply wrong.

**The message carries an id and a status, not the order.** A push carrying names, prices and
addresses would be a second copy of the order contract that never passes through the query filters,
so a mistake in a group name would leak a customer's address rather than an id they already have.
And a payload goes stale the moment it is sent; a screen that refetches shows what is in the
database now.

**A bearer token in a query string, in exactly one place.** A browser cannot set an `Authorization`
header on a WebSocket, so SignalR sends the token as `?access_token=`. That is accepted on the hub
path and nowhere else: query strings end up in proxy logs, browser history and referrer headers, so
it is tolerated where the browser leaves no choice and refused everywhere else. A test posts a valid
token to a normal endpoint that way and insists on a 401.

**Nothing is pushed inside a transaction, and a failed push never fails a request.** A message
cannot be rolled back, so telling a kitchen about an order that then failed to save would have them
cooking food nobody ordered. The mirror image matters as much: a socket that has gone away must not
turn a committed order into a 500, or the customer tries again and the kitchen cooks it twice.

**The one contract no generated client checks.** SignalR is not in the OpenAPI document, so the
TypeScript handler is written by hand — and a casing mismatch fails nothing, it just hands the
screen `undefined`. A test reads the raw payload off the wire and asserts the property names and
that the status is a number, matching the generated numeric enum.

**6b — the screen's end ✅.** A shared `realtime` library holding one service, `OrderStream`.

**One signal, not two mechanisms.** A screen watches `revision` and refetches whenever it changes.
That number goes up on a pushed message, on every poll tick, and the moment a dropped connection
comes back — so "live updates" and "polling fallback" are one thing a screen consumes rather than
two code paths it has to keep in agreement. The screen never asks whether the socket is up, only
whether it is behind. Nothing in the library fetches anything: what to refetch is the screen's
business, and a stream that knew about the orders query would need changing every time a screen
wanted something else.

**The bump on reconnect is the part that is easy to leave out.** A tablet that loses signal for two
minutes misses every message sent in those two minutes, and SignalR replays none of them. A screen
that only listened to pushes would come back looking current and be wrong until the next order
happened to arrive.

**The poll runs even while the channel is up**, once a minute against ten seconds while it is down.
A push the server failed to send is swallowed and logged there — deliberately, so a dead socket
cannot fail a committed order — which means without a backstop the screen would never learn about
the one order it missed.

**Reconnection never gives up.** SignalR's built-in policy stops after about thirty seconds, and a
tablet propped up in a kitchen that quietly stopped trying after half a minute of no signal is
exactly the screen this step exists to prevent. The first connect is handled separately, because
automatic reconnect only covers a connection that was once up — and the API being down when a
kitchen opens its tablet in the morning is not exotic.

**The token is checked for expiry before the handshake, not after.** An HTTP request can afford to
send a stale token and recover from the 401 by replaying itself. A WebSocket handshake gets one
attempt, and SignalR treats its failure as a connection problem rather than something to refresh
and retry.

**A test that passed for the wrong reason, caught while writing it.** "Exchanges an expiring token"
asserted only that the token differed from the expired one — and passed because no refresher was
provided, the exchange threw, and the catch returned an empty string. It now stubs the refresher
and asserts the refreshed token by name, plus that a still-good token is not exchanged at all.

**A gap in the generated client, fixed first in its own commit.** Every enum reached the TypeScript
client as a bare `number`, because an enum arrives in the OpenAPI document as `{"type": "integer"}`
with its names dropped. A screen accepting an order would have posted `{ to: 2 }` — a magic number
that means something else the day somebody renumbers the C# enum, with nothing to catch it.

### Step 7 — Kitchen queue

The screen staff live in during service. Orders grouped by status, the next action on each one as
a single press. Loud about what needs attention: a new order, an order waiting too long, one about
to breach its promised time.

Designed for a tablet propped up in a kitchen, read at arm's length by someone with their hands
full.

Split in two: the board, then acting on it.

**7a — the board ✅.** Four columns — New, Accepted, Cooking, Ready & on the way — with the rows
the API sends and the urgency the clock makes of them.

**Oldest first, which contradicts what this plan said.** "Newest first" was written for a history
list and copied onto the queue by mistake. A kitchen works the order that has waited longest, and
it has to be the server that decides: newest-first paging would put the orders most in need of
attention on the last page. The customer's history is still newest first, because they are looking
for last night's order.

**One trigger, and the board never asks which.** It refetches when `OrderStream`'s revision
changes — a pushed message, a reconnection, or the poll behind them, folded into one number by
step 6. Verified against the running stack: an order placed entirely outside the browser appeared
on the board in under half a second with no reload.

**A clock of its own, separate from the refresh.** Urgency is a comparison against the time now,
so an order sitting unanswered becomes late with nothing happening on the server. Without its own
tick the board would still be calling an order calm twenty minutes after it stopped being calm.

**Thresholds, each a judgement rather than arithmetic.** An unanswered order asks for attention at
two minutes and is late at five, measured from placement, because nobody has said yes to the
customer yet. Everything else is measured against the promised window and stops counting once the
food has left the pass — a countdown still running on an order on a moped would have the board
shouting about something the kitchen has finished.

**Amber, not the theme's tertiary.** Material 3 has no token for "this wants a person soon", and
the nearest one is configured as blue here — which on a kitchen screen reads as a link. The board
carries three states and only two of them have colour, so that the colour still means something.

**A failed refresh leaves the rows alone.** A kitchen mid-service is far better off with a board
that is a few seconds stale and says so than with an empty one.

**Two bugs caught by looking at it, and both now caught by a build.** The board wrapped to three
columns and dropped the fourth underneath the first, and the Cooking column had no icon at all —
`skillet` exists in Material Symbols but not in the classic font this application bundles, so it
rendered as nothing, with no error anywhere. That is the second time an icon has failed silently
in this project. Icon names in TypeScript are now typed against the bundled font's own manifest,
so a wrong one is a compile error, and a spec reads every `<mat-icon>` in every template and
checks it against the same list — with a guard test, because a regex that stops matching would
otherwise leave the check passing on an empty set.

**7b — acting on it ✅.** Every card carries the moves it may make, and pressing one is a single
tap.

**The buttons come from the API, not from the column.** Each queue row now carries its own
`availableTransitions` — the same list the detail already returned, from the same transition
table. Working them out on the screen would have been a second copy of the rule, and the first
copy is the one the server enforces; a screen that guessed would draw a button that gets a 403.
It costs the database nothing: it is a lookup in a frozen table, done after the query.

**The board decides only the wording.** Accept, Refuse, Start cooking, Ready, Send out — and, in
the one place wording depends on more than the status, "Collected" at a counter against
"Delivered" at a door. Telling a pickup customer their order was delivered is a small lie a
kitchen has to explain on the phone.

**A refusal opens a form; nothing else does.** Which moves need a reason is the state machine's
answer, not the screen's, and the fixed list is what the rejection-rate report groups by. The note
beside it is always optional — asking a busy kitchen to write an essay is how you get "asdf".

**A new order makes a noise.** Colour tells somebody what needs attention once they look; this is
what makes them look, on a tablet propped across the room. Synthesised rather than played from a
file: two hundred bytes instead of an asset to fetch over a Lebanese connection, and it cannot
404. Browsers refuse audio until the page has been touched, so the toggle says "tap any order
button once" rather than claiming to be on while the page is silent.

**A bug the tests found, and a good one.** A refused press — another tablet accepted it first —
set the error and then immediately reloaded the board, and a successful reload clears the error.
The message vanished a heartbeat after appearing, so the person saw a button do nothing and was
told nothing. The refusal is now set after the reload, deliberately, with a comment saying so.

**Two more found by pressing the buttons in a browser.** The primary action was not filled: the
appearance was bound through `attr.matButton`, which sets an HTML attribute Material never reads,
so every button came out identically outlined and the common action stopped being findable. And a
card that was late to be *answered* showed its promised time in red — saying the promise was blown
when it had eleven minutes left.

Verified against the running stack: Accept moved an order between columns, Refuse opened the
dialog, and the reason chosen there — not the pre-selected one — is what landed on the order.

### Step 8 — Order history and detail ✅

The other half of the dashboard: what happened yesterday, one order in full with its event
trail, and why an order was rejected.

**One receipt, opened from both screens.** It takes an order id, not a row, and loads the order
itself — a caller handing over the summary it already has would be handing over a copy as old as
its last refresh, and this is the screen somebody opens precisely because they want to know
exactly what happened. It is read-only: the buttons live on the queue card behind it, and in the
history every order is finished and offers no moves at all.

**Why it was refused comes first.** It is the one question somebody opens a refused order to
answer, and burying it under the receipt would make them hunt. The reason is also on the history
row itself: somebody counting how often the fryer went down should not have to open twenty orders
to find out, and three "out of stock" in an hour says something one order at a time does not.

**The history holds still.** The queue refreshes itself because a kitchen mid-service cannot be
asked to press anything; a history is read deliberately, and a list that reordered itself
mid-scroll would be worse than one a minute old. It also blanks its rows when a load fails, where
the queue keeps them — a history still showing the previous filter's rows is answering a question
nobody asked, while a stale kitchen board is better than an empty one.

**The same list, read from the other end.** A queue is worked oldest-first and a history newest-
first, and with paging that is not something a client can fix afterwards: the wrong end opens on
the restaurant's first ever order. The endpoint takes it as a parameter rather than guessing from
the statuses asked for.

**Wording is a second copy, deliberately, and the rules are not.** The same status is "Refuse" on
a button, "Refused" on a chip, "Waiting to be accepted" under an order number and "Placed" in the
trail — one string shipped from the API would be wrong in three of those four places. What is
never duplicated is which moves are legal, and that still comes from the transition table.

**The bug that made the client's types honest.** Every optional field was typed `| undefined`
while the runtime returned `null` — the response is `JSON.parse`d straight through, and JSON has
no undefined. A strict `=== undefined` check put a "could not be completed / no reason given"
panel on every order that had never been refused, and the compiler was happy because the type said
the field could only ever be undefined. Fixed at the generator, in its own commit; the compiler
then pointed at all twelve places relying on it, one of which had already been worked around
locally instead of fixed.

**A date filter is deliberately not here.** "What happened yesterday" is served by newest-first
paging, and filtering by day is a reporting concern that belongs with the rest of them in phase 4.

**Not fixed by the screenshots:** an E2E run failed once, on a test unrelated to this step, while
the dev server was still recompiling files that had just been formatted. It passed on both re-runs
and in a clean run afterwards. Worth naming rather than quietly re-running.

### Step 9 — E2E and phase close ✅

Place an order through the API, watch it appear in the kitchen queue, accept it, walk it to
delivered, and assert the event trail records each step with the account that made it. Plus the
refusals.

**A restaurant that never closes, added to the seed.** The checkout refuses an order to a shut
kitchen — correctly — and a browser test has no clock it can move, which is what made the
integration suite's `TestClock` necessary in step 5a. Every seeded restaurant closed for between
ten and seventeen hours a day, so every order test here would have passed or failed depending on
the hour CI happened to run. Shawarma Station is now open around the clock, which is realistic in
Beirut and gives the whole suite a dependable subject. FriesLab keeps its noon-to-two window
precisely because two integration tests turn on it.

**"Open all day" is not 00:00–23:59.** The domain said it was; it is shut for the last sixty
seconds of every day, because the comparison against the closing time is exclusive. That is a
failure once in every fourteen hundred and forty runs, at an hour nobody is awake to reproduce.
The seed writes the widest window a `TimeOnly` holds, and two domain tests now pin both facts —
including the sixty-second hole, so nobody tidies the seed back to the obvious spelling.

**What the end-to-end suite proves that nothing else can.** An order placed by a customer's app
appears on a kitchen screen nobody touched; four presses walk it to delivered; the trail afterwards
names the account behind every step. Deleting the board's subscription to the live stream fails
that first test, and making the reason dialog ignore what was chosen fails the refusal test — so
neither is passing by coincidence.

**A refusal the test could not reach at first.** "A dish sold out while the basket sat" was written
as add-then-refuse and got "your basket is empty" instead: adding an item that is *already* off is
refused at the basket. The refusal only exists in the gap between deciding and paying, so the
helper now stocks a basket and checks out as two steps, and the test closes the shop in between.

**The closed-restaurant refusal is deliberately not here.** It is real, and it is tested — in the
integration suite, on a clock that can be moved. Recreating it in a browser would mean either
waiting for a particular hour or asserting nothing for most of the day. Naming that is better than
a test that quietly passes because it never ran the assertion.

---

## 7. Phase 3, closed

An order can now be placed, priced, refused for six specific reasons, accepted, cooked, handed
over, and read back afterwards with its whole history — and a kitchen can do all of it from a
tablet without pressing refresh.

**What this phase actually changed about the product.** Phase 2 could describe food. This one can
sell it.

**The rules live in one place each, and the tests are what keep them there.** Which moves are legal
is the transition table, enumerated by a truth table over all 256 combinations. What an order cost
is `OrderPricing`, on the server, with the client's figure used only to check agreement. Who may
see an order is the query filter; who may move it is the state machine. Every screen that draws a
button asks the API which buttons exist.

**Bugs this phase found in itself,** which is the part worth keeping: two tablets accepting the
same order; a validator that allowed 1000 characters into a 500-character column; SignalR pushing
the same order twice to somebody who was both staff and customer; a generated client whose types
claimed `undefined` where the wire sends `null`; every enum reaching TypeScript as a bare number;
an icon that rendered as nothing; a refusal message wiped out by the reload that followed it. All
found by a test or by looking at the running screen, none by a user.

**What is deliberately not built.** No stock levels — `IsAvailable` is the switch a kitchen
actually uses. No payment gateway — cash on delivery is the Lebanese default and the interface is
what makes a processor a swap. No date filtering on the history, and no way for a restaurant to set
its own opening hours through the product: both are settings and reporting, which is phase 4.

**One limitation named and left alone.** The query filter gives staff their restaurant's orders
*instead of* their own, so a restaurant owner who orders from a different restaurant cannot see
that order. It is the security boundary ADR-07 is built on, and widening it deserves its own
review rather than a change made in passing.

---

## 4. Decisions, with my recommendations

Answered the same way as Phase 2: taken, not asked, unless the answer changes what gets built.

| # | Question | Recommendation |
|---|---|---|
| 1 | **Live updates: SignalR or polling?** | **SignalR.** A poll that is quick enough for a kitchen hammers the API all service; one that is gentle enough for the API misses orders. Falls back to polling on disconnect |
| 2 | **Cart on the server or in the browser?** | **Server.** The tables exist for it, it survives switching devices, and pricing has to be server-side regardless — a browser cart would be recomputed on arrival anyway |
| 3 | **What happens if a price changed while the cart sat?** | **Refuse the checkout and show what changed.** Silently charging the new price is the worst option; silently honouring the old one lets a stale tab set the price |
| 4 | **Can a customer cancel after the restaurant accepts?** | **Yes, until cooking starts.** This plan first said "only while Placed", which contradicted the `OrderStatus` enum written in Phase 1 — "Cancelled: only reachable from Placed or Accepted". The enum is right: *Accepted* means somebody saw the order, *Preparing* means food is being made, and that is the line worth drawing. Corrected in step 1 |
| 5 | **Does stock get decremented?** | **No.** There is no stock model — `IsAvailable` is the switch a kitchen actually uses. Inventory is a different product |
| 6 | **Mock payment now, or leave it?** | **Mock now, behind an interface.** Cash on delivery is the real Lebanese default and needs no gateway; the interface is what makes a processor a swap rather than a rewrite |

---

## 5. How this gets reviewed

The same as Phase 2, which worked: small commits, each one a step, each one with its own tests
and — for anything with a screen — a screenshot taken from the running stack.

Two additions for this phase:

**The state machine gets a truth table.** Every from/to pair, legal or not, asserted in one test
file. It is the cheapest possible way to make an illegal transition impossible to add by accident.

**Money gets exact arithmetic.** No floating point anywhere near a total, and a test that a
placed order's stored total equals the sum of its lines to the cent.

---

## 6. Sequence

Steps 1 to 5 are backend and run in order — each genuinely needs the one before it. Step 6 can
start once step 5 exists. Steps 7 and 8 are screens and could be built in either order; the
kitchen queue comes first because it is the one that earns money.

Nothing here needs the storefront, which is still Phase 5. Orders are placed against the API in
this phase and through a customer's browser in that one.
