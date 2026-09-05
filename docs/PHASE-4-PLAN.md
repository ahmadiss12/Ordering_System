# Phase 4 — Running a restaurant

Phase 2 gave a restaurant a menu. Phase 3 let it sell from that menu. This phase is what a
restaurant needs before either of those is any use to anybody but us: the ability to say when it
is open, where it delivers, what that costs, and who else may sign in.

---

## 1. What "done" looks like

A restaurant that has never been seeded can be set up through the product. Its owner signs in,
sets opening hours, picks the zones they deliver to and what each costs, sets a prep time and a
minimum order, adds their staff, and starts taking orders — without anybody running SQL.

A platform admin can see every restaurant, turn one off, set its commission, and answer the
question the whole rejection-reason column was built for: which restaurants are refusing orders,
and why.

---

## 2. Why this phase and not the storefront

The storefront is Phase 5 and it is the more exciting one. It goes second on purpose.

Today the product has exactly the three restaurants the seeder wrote. **There is no way to onboard
a fourth.** Hours, zones, fees, prep time and staff accounts are all seed data, and the Settings
screen has been a placeholder since Phase 2 promising the endpoints that would fill it. A
storefront built on top of that would be a shop window for a shop nobody else can open.

The order of work is: make it operable, then make it public.

---

## 3. Where this differs from Phase 3

Phase 3 was one long flow with a state machine in the middle. This phase is mostly CRUD again —
but three things stop it being Phase 2 with different nouns:

**Every field here changes what customers are charged.** A delivery fee, a minimum order, a
commission percentage. Phase 3 established that an order snapshots its own copy of all of these, so
editing them must never restate history — and the tests have to prove that rather than assume it.

**Two levels of authority, not one.** An owner sets their own hours; only a platform admin sets
their commission. The role split has existed since Phase 1 and been enforced on exactly one
placeholder screen. This is where it becomes real.

**Someone can lock themselves out.** An owner removing the last owner, or closing every day of the
week, are both one press away and both leave a restaurant that cannot be recovered from inside the
product.

---

## 4. Steps

### Step 1 — Restaurant profile and service settings ✅

The plain ones first, because everything else is a screen on top of them: name, description,
phone, prep time, minimum order, and the accepting-orders switch a kitchen flips during a rush.

**No restaurant id appears in any route.** It comes from the token, so there is no id for a caller
to change — ADR-07's explicit half, since a query filter is a WHERE clause and an UPDATE that
trusted an id in the URL would edit somebody else's restaurant. The isolation test has no id to
tamper with either, so it sends one restaurant's values as another owner and checks whose row
moved.

**Two policies on one controller.** Reading and the pause switch are open to any staff member;
the name, the phone, the prep time and the minimum order are the owner's. Commission and the
platform's active switch are on the response and absent from the request: a restaurant is entitled
to see what it is being charged, and neither figure is theirs to set.

**The slug is not editable.** It is the address of the restaurant's public page, so changing it
breaks every link anybody has shared. A rename is a support conversation, not a form field.

**A design mistake caught by an existing test.** Opening the Settings screen to staff — so a cook
could reach the pause switch — left the application with no owner-only section at all, and the
shell spec that proves the role split failed on an empty list. The better answer was already
implied by the plan: the switch belongs on the **queue**, which is the screen a cook is already
looking at, and Settings stays owner-only. Two entry points, one endpoint, each where its user is.

**What a change here must never reach.** Two tests place an order, change the minimum and the prep
time, and assert the placed order's total and promise are untouched. That is the property Phase 3
built the snapshot columns for, and an edit on this screen is exactly what could quietly break it.

**A comment corrected.** `CheckoutService` said "the online gateway is Phase 4", written before
this plan existed. Cash on delivery blocks nobody in Lebanon; not being able to set your own
opening hours does. Payments moved to their own phase and the comment says so rather than
misleading the next reader.

### Step 2 — Opening hours ✅

The one with a real domain behind it already: several windows a day, and windows that cross
midnight. `OpeningHours.IsOpenAt` was written in Phase 2 and had been fed only seed data since.

**The week is replaced whole.** What is being edited is a week — whether two windows clash, and
whether anything is left at all, are questions about the set — so a row-at-a-time endpoint would
validate the same set anyway while letting a client build an invalid week one request at a time.
The rows have no identity anybody refers to, so nothing is lost by rewriting them.

**Overlap detection went into the domain, on one weekly timeline.** Every window is laid out in
minutes from Monday midnight, so a window that runs past midnight is compared against the next
morning's rather than only against its own day — Monday 18:00–02:00 and Tuesday 01:00–05:00 both
cover Tuesday at half past one, and a day-by-day check would never see it. Sunday night wraps to
Monday, which is the case an implementation is most likely to miss and has a test of its own.

**Touching is not overlapping.** Noon to four and four to eight is a normal way to describe a day.

**Overlaps are refused even though nothing downstream breaks.** `IsOpenAt` returns true if any
window matches, so an overlap changes no behaviour at all — which is exactly why it is worth
refusing at the point somebody types it. A restaurant that entered 12:00–16:00 and 14:00–20:00
meant 19:00, and nothing would ever have told them.

**An empty week needs saying so.** No hours means shut to customers indefinitely, which is a real
thing to want — a kitchen closing for August — and also what a screen looks like halfway through
an edit. The API distinguishes them with a confirmation flag on the request rather than trusting
a dialog it cannot see, and the screen shows that dialog.

**The overnight case is said in words.** A close time earlier than an open time is not a mistake,
it is how "noon until two in the morning" is written — so the moment somebody types it the row
says "closes 02:00 the next day" rather than leaving them to work it out.

**A flaw the running stack exposed.** With the API on a stale build the editor drew seven rows
saying "Closed" under an error message — a confident answer to a question it could not answer. The
week is now drawn only once it has actually arrived, and a test pins it.

### Step 3 — Delivery zones and fees ✅

Which zones a restaurant serves, the fee for each, and the travel minutes that feed the promised
time. The zones themselves are platform-owned — a restaurant picks from them rather than inventing
its own Hamra, which is what makes a customer's saved address and a restaurant's coverage
comparable at all.

**A zone at a time, unlike the hours, and for a reason.** Hours are a set with relationships inside
them — two windows can clash — so they are only meaningful whole. Zones are independent: serving
Hamra says nothing about Achrafieh and nothing can conflict. So the endpoint takes one zone, and
each row on the screen saves itself. A row that fails is then a row that failed, rather than one
unidentified failure somewhere in a grid of ten.

**Every zone is listed, served or not.** A restaurant cannot pick a zone it does not know exists,
and a list of only the configured ones would make adding the first one impossible to find.

**Switching a zone off keeps its terms.** That is what `RestaurantZone.IsActive` was for, and the
screen leaves the numbers visible rather than hiding them — seeing them is what says that turning
the zone back on after a fortnight is one press and not a re-entry. Null fees mean something
different from zero ones: "we have never set terms for Jounieh" against "we deliver there free".

**A zone never configured starts at something plausible.** Free delivery in no time at all is a
promise nobody meant to make by pressing one switch.

**Free delivery is allowed on purpose.** Zero is a real offer, and a restaurant wanting to make it
should not have to charge a cent to satisfy a validator.

**The change followed through to a customer.** Two tests do the thing this step exists for:
suspending a zone somebody has a saved address in, and watching their next order be refused by
name; and raising a fee, watching the next quote move, and watching an order already placed not.

### Step 4 — Staff accounts ✅

An owner invites staff, sets their role, and removes them. The `RestaurantStaff` row is what puts
the `restaurant_id` claim in a token, so this is the most security-sensitive screen in the phase —
adding a row here grants somebody access to a tenant's orders.

**A prerequisite came first.** The Order query filter read "a customer with no restaurant claim
sees their own orders", so being hired erased your own order history. That was tolerable while
staff were seed data and intolerable the moment an owner could invite by email — the person being
hired is usually already a customer here. It now reads "everybody sees their own", which widens
nothing except a caller's own rows.

**One account, not two.** An invited address that already has an account is reused. A second
account would strand the person's history on an address they can no longer sign in with. An
unknown address gets an account holding a hash of a discarded secret — no password anybody knows —
and a link to choose one, valid for days rather than the reset link's hours: somebody recovering
their own account is waiting at the screen, somebody being hired may not read their mail until
their next shift.

**One restaurant per person, for now.** The composite key allows two memberships but a token
carries one `restaurant_id` and nothing lets its holder choose, so a second row would leave the
tenant to whichever the database returned first. Invitations refuse it, and the claim query is
ordered so the answer is at least defined. Switching between restaurants is its own feature.

**Two role systems, moved in one place.** The global `RoleType` decides which endpoints a token
opens; the per-restaurant `StaffRoleType` decides what somebody is here. Set one without the other
and you get an owner who cannot open the owner screens, or a demoted owner who still can.

**The last-owner rule, and the rule that nearly hid it.** An earlier draft also banned acting on
your own account. It read as prudent and made the last-owner check unreachable: every route to
demoting an owner requires a second owner, so the target was never the last. A mutation to the
count changed nothing and no test failed. Self-service is allowed, the count is what refuses, and
an owner can hand over by promoting a successor and then stepping back.

**A mail server that is down does not undo an invitation.** The row is committed before the email
is attempted, so an exception told an owner nothing had happened while somebody had just been
granted the order book. The send is best-effort and the response says whether it worked, keeping
"emailed", "nothing to send" and "should have been sent and was not" apart.

### Step 5 — The platform admin ✅

The other side of the two-level split: every restaurant, active or not, with commission and the
active switch. Small, and deliberately separate from everything above — an owner must never reach
these fields, and a screen that draws them for the wrong role is how that happens.

**The guard needed a new method.** Every other write path uses `EnsureCanActFor`, which admits a
restaurant acting on itself. That is right for a menu and wrong for a commission rate: an owner
posting their own restaurant's id would have passed it. `RequirePlatformAdmin` is what these three
endpoints call.

**The second layer was nearly decorative.** Swapping that guard back to `EnsureCanActFor` broke
nothing and failed nothing, because the controller's policy refuses an owner before the service is
entered — so no test through HTTP could ever say whether the service checked anything. Two tests
now construct the service directly with a staff tenant. This is the same shape as the last-owner
rule in Step 4: when one guard sits in front of another, only the outer one is under test unless
something deliberately goes round it.

**Neither field reaches backwards.** A rate applies to the next order and no earlier one, and both
halves of that are followed through to real orders — the placed one keeps its rate, the next one
gets the new one. A rate change that reached back through history would look exactly like a bug
and would only be noticed at the end of the month.

**Hiding a restaurant does not stop its kitchen.** Customers cannot find, quote or order from it;
orders already placed are untouched and its staff keep working them. People waiting for food they
have paid for should get it whatever the platform has decided about the listing.

**The screen says what each press does before it happens**, including what it does *not* do, and
shows the live order count next to the switch — hiding a restaurant with nine orders cooking is a
different act from hiding one with none, and nothing else on the page would have said so.

**Not built, and worth naming:** nothing records who changed a rate or when. For a field that
decides what a restaurant is paid, an audit trail is the obvious next thing, and it needs a table
rather than a corner of this step.

### Step 6 — Reporting ✅

What the rejection reasons were collected for. Orders by day, revenue and commission, and a
rejection rate per restaurant with the reasons broken out.

Date filtering on the order history lands here too — Phase 3 left it out on purpose, saying it
belonged with the rest of the reporting rather than bolted onto a queue endpoint. It was right to
wait: the filter and the report have to agree about which day an order belongs to, and settling
that question is most of this step.

**Orders now carry the day the kitchen worked.** Checkout already computed it to build the order
number and then threw it away, so a report grouped on `PlacedAt` — which is UTC — would have filed
part of every Beirut evening under the previous day, and differently in winter and summer. Storing
it means the day a report puts an order under is the day printed in the order number the customer
quotes. A check constraint rejects an order without one: EF sends every mapped column on insert,
so `DateOnly`'s own 0001-01-01 would arrive as a value and a column default would never fire — the
order would vanish from every report rather than fail.

**Revenue is what was kept.** Refused and withdrawn orders are not sales; both are counted
separately so the figures reconcile. Orders still cooking count, because a report that waited for
delivery would read as empty right through service, which is when somebody looks at it. The
average divides by the orders that produced the revenue, not by every order placed.

**A cancellation is in the denominator of the rejection rate and out of its numerator.** A
customer changing their mind is not the restaurant refusing them.

**Commission comes from each order's own snapshot**, so last month does not move when a rate is
renegotiated. **Every day in the range is returned**, empty ones included — a missing day draws as
a gap, and a gap reads as missing data rather than as a quiet Tuesday.

**Calendar dates reach TypeScript as strings.** NSwag renders `format: date` as a JavaScript
`Date`, which is an instant: sending one calls `toISOString()`, so an owner in Beirut picking the
5th posts the 4th at 21:00Z, and reading one parses `"2026-09-05"` as midnight UTC. Both put back
the exact bug the business date exists to prevent, at the client boundary. A schema transformer
describes `DateOnly` as a plain string, because NSwag's own `dateType` setting is ignored by this
version.

**Two plots, one day axis** — never two y-scales on one. Orders and revenue are measures of
different size, and a shared axis would invent whatever relationship the scales implied. The four
headline figures are stat tiles rather than charts.

**Three defects only rendering it found**, none of which a test would have caught: an axis step
list coarse enough to send a 27-order day to an axis of 50, a pinned SVG height that stretched
both charts 1.6x horizontally, and a day-label row that lined up with the plot at exactly one
window width.

### Step 7 — E2E and phase close ✅

Set up a restaurant from nothing through the product alone: hours, a zone, a fee, a staff account,
then place an order against it and have the kitchen work it. If that passes, the product can
onboard a customer.

**It passes.** `web/e2e/onboarding.spec.ts` walks it: the platform lists Saj Corner and settles its
commission, its owner sets hours, a delivery zone and fee, hires somebody and adds a dish, and
then a customer reads the public menu, orders that dish, and the kitchen takes it to delivered.
The accounts are handed over by signing out, not by swapping tokens — the guard that keeps a
signed-in visitor off the login page is part of what is being claimed.

**The seed gained a restaurant with nothing set up.** Hidden, no hours, no zones, no menu. That is
the state a restaurant is in between the platform creating it and its owner configuring it, and it
existed nowhere: three finished restaurants cannot show it. The journey resets it each run, which
is what makes "from nothing" true on the second run as well as the first.

**Writing it found a live bug.** Opening the hours screen and pressing Save without touching
anything shut an around-the-clock restaurant for the last minute of every day: the form held times
as `HH:mm`, so the closing value that means "does not close" round-tripped to 23:59, which is a
real closing time. No owner could enter the hours the seeded always-open restaurant had. The
screen now says the words instead of showing two boxes it cannot fill.

**What the journey cannot prove, and why.** The delivery fee reaches no customer's bill in it,
because a customer has no way to have an address yet — that endpoint arrives with the storefront
in phase 5. The zone step proves the fee round-trips through the server; the integration suite
proves the rest against the database.

---

## 7. What this phase left undone

Named rather than quietly dropped.

| | |
|---|---|
| ~~**A restaurant cannot be created through the product.**~~ Closed after the phase: `POST /api/platform/restaurants` takes one on, hidden and unconfigured, and invites its first owner through the same path a colleague is invited by. |
| **Nothing records who changed a commission rate, or when.** For a field that decides what a restaurant is paid, an audit trail is the obvious next thing; it needs a table rather than a corner of a step. |
| **An invitation cannot be re-sent.** The recovery path is to remove the person and invite them again, which issues a fresh link and is tested — but it is a workaround wearing the shape of a feature. |
| **One person, one restaurant.** The composite key allows two memberships; an access token carries one `restaurant_id` and nothing lets its holder choose. Invitations refuse the second rather than let the database decide. Switching between restaurants is its own feature. |
| **Online payments.** Decision 1 moved them to their own phase. Cash on delivery blocks nobody in Lebanon. |

---

## 5. Decisions, with my recommendations

Taken rather than asked, unless the answer changes what gets built.

| # | Question | Recommendation |
|---|---|---|
| 1 | **Where did online payments go?** | **Not here.** A comment in `CheckoutService` said "the online gateway is Phase 4", written before this plan existed. Cash on delivery is the Lebanese default and works today; nothing about payments blocks a restaurant from operating, while all of the above does. It moves to its own phase, and that comment gets corrected rather than left to mislead |
| 2 | **Can an owner change their own commission?** | **No.** It is the platform's revenue and the restaurant's cost — the one field both parties care about and only one may set |
| 3 | **Staff by invitation or by direct creation?** | **Invitation by email.** Creating an account with a password an owner chooses means the owner knows their staff's password. The reset-token machinery from Phase 1 already does most of this |
| 4 | **Can an owner remove the last owner?** | **No, refused with a reason.** A restaurant with no owner cannot be repaired from inside the product, and the alternative — a platform admin doing it by hand — is the support ticket this rule prevents |
| 5 | **Can a restaurant close every day of the week?** | **Yes, but it must say so.** Deleting all hours is how a kitchen goes on holiday. The screen has to make "closed indefinitely" a deliberate state rather than something that happens by deleting rows one at a time |
| 6 | **Does changing a fee affect orders already placed?** | **Never, and there is a test for it.** Phase 3 snapshots every price onto the order; this phase is where somebody could accidentally break that, so it gets proved rather than trusted |

---

## 6. Sequence

Steps 1 to 4 are independent of each other and can be built in any order; the sequence above is
the one an owner would meet them in. Step 5 needs nothing. Step 6 needs orders to report on, which
Phase 3 provides. Step 7 needs all of them.
