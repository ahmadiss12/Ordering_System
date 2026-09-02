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

**Left open by this step.** A restaurant that accepts an order and then cannot fulfil it — a
power cut, an ingredient gone — has no way out. `Rejected` is reachable only from `Placed`, and
`Cancelled` belongs to the customer. The transition table says so deliberately rather than
inventing a move the schema never described. It needs a decision before the kitchen queue in
step 7 puts a button on a screen: either a restaurant-initiated cancellation from `Accepted` and
`Preparing`, or an explicit answer that it stays a phone call.

### Step 2 — Cart

Server-side, keyed by customer and restaurant, because the tables were designed that way and a
cart that lives in one browser is lost the moment someone switches to their phone.

Add a line with its chosen options, change a quantity, remove it, empty the cart. The **selection
rules from Phase 2 are enforced here**: a group that says "choose 1" rejects a line with two.

### Step 3 — Pricing

One place that computes a total, on the server, and a quote endpoint so the client can show the
same number before committing. The client never sends a price.

Subtotal from lines and option deltas, delivery fee from the zone, commission for the platform's
books, promised time from the restaurant's prep minutes. Tax stays zero — the column exists for
the day Lebanese VAT applies.

### Step 4 — Checkout

The step that turns a cart into an order, and the one with the most ways to fail:

- minimum order not met
- restaurant closed, or not accepting orders
- delivery address outside every zone the restaurant serves
- an item unavailable or deleted since it went into the cart
- a price changed since the cart was built

Each is a specific, readable refusal, not a generic 400. On success: allocate the daily order
number, snapshot every name and price, write the first `OrderEvent`, empty the cart — in one
transaction.

### Step 5 — Order endpoints

Reading and moving orders, with the tenant guard on every one.

Customer: place, list mine, one order in detail, cancel while it is still cancellable.
Restaurant: the queue, accept, reject with a reason, advance status.

### Step 6 — Live updates

A kitchen screen that misses an order is worse than no screen. SignalR, scoped by the same
`restaurant_id` claim the query filters use, with polling as the fallback when a connection
drops — a phone on Lebanese mobile data will drop.

### Step 7 — Kitchen queue

The screen staff live in during service. Orders grouped by status, newest first, the next action
on each one as a single press. Loud about what needs attention: a new order, an order waiting too
long, one about to breach its promised time.

Designed for a tablet propped up in a kitchen, read at arm's length by someone with their hands
full.

### Step 8 — Order history and detail

The other half of the dashboard: what happened yesterday, one order in full with its event
trail, and why an order was rejected.

### Step 9 — E2E and phase close

Place an order through the API, watch it appear in the kitchen queue, accept it, walk it to
delivered, and assert the event trail records each step with the account that made it. Plus the
refusals — a below-minimum order and a closed restaurant.

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
