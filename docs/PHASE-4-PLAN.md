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

### Step 2 — Opening hours

The one with a real domain behind it already: several windows a day, and windows that cross
midnight. `OpeningHours.IsOpenAt` was written in Phase 2 and has been fed only seed data ever
since.

An editor has to make the overnight case sayable without a manual, and has to refuse a set of
hours that would silently close the restaurant forever.

### Step 3 — Delivery zones and fees

Which zones a restaurant serves, the fee for each, and the travel minutes that feed the promised
time. The zones themselves are platform-owned — a restaurant picks from them rather than inventing
its own Hamra.

Turning off a zone a customer has a saved address in is the interesting case: their next order is
refused with "we don't deliver there", which Phase 3 already says properly.

### Step 4 — Staff accounts

An owner invites staff, sets their role, and removes them. The `RestaurantStaff` row is what puts
the `restaurant_id` claim in a token, so this is the most security-sensitive screen in the phase —
adding a row here grants somebody access to a tenant's orders.

The last-owner rule lives here.

### Step 5 — The platform admin

The other side of the two-level split: every restaurant, active or not, with commission and the
active switch. Small, and deliberately separate from everything above — an owner must never reach
these fields, and a screen that draws them for the wrong role is how that happens.

### Step 6 — Reporting

What the rejection reasons were collected for. Orders by day, revenue and commission, and a
rejection rate per restaurant with the reasons broken out.

Date filtering on the order history lands here too — Phase 3 left it out on purpose, saying it
belonged with the rest of the reporting rather than bolted onto a queue endpoint.

### Step 7 — E2E and phase close

Set up a restaurant from nothing through the product alone: hours, a zone, a fee, a staff account,
then place an order against it and have the kitchen work it. If that passes, the product can
onboard a customer.

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
