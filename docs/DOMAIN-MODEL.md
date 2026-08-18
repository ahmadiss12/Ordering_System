# Domain Model

**Status:** Proposal for review — no code written yet
**Companion to:** `ARCHITECTURE.md`
**Covers:** 24 entities across five clusters

Nullable columns are listed under each diagram rather than marked inside it, so the
diagrams stay readable. Exact types, indexes and constraints land in Step 4.

---

## 1. Identity, tenancy and auth

```mermaid
classDiagram
    class User {
        Guid Id
        string Email
        string PasswordHash
        string FullName
        string Phone
        bool IsActive
        DateTime CreatedAt
    }
    class UserRole {
        Guid UserId
        RoleType Role
    }
    class RefreshToken {
        Guid Id
        Guid UserId
        Guid FamilyId
        string TokenHash
        DateTime ExpiresAt
        DateTime UsedAt
        DateTime RevokedAt
    }
    class PasswordResetToken {
        Guid Id
        Guid UserId
        string TokenHash
        DateTime ExpiresAt
        DateTime UsedAt
    }
    class Restaurant {
        Guid Id
        string Name
        string Slug
        string Description
        string LogoUrl
        string Phone
        bool IsActive
        bool IsAcceptingOrders
        decimal CommissionPercent
        decimal MinOrderUsd
        int DefaultPrepMinutes
    }
    class RestaurantStaff {
        Guid UserId
        Guid RestaurantId
        StaffRoleType StaffRole
    }
    class RestaurantHours {
        Guid Id
        Guid RestaurantId
        DayOfWeek DayOfWeek
        TimeOnly OpenTime
        TimeOnly CloseTime
    }
    class PlatformSetting {
        string Key
        string Value
        DateTime UpdatedAt
        Guid UpdatedByUserId
    }

    User "1" --> "*" UserRole : holds
    User "1" --> "*" RefreshToken : issued
    User "1" --> "*" PasswordResetToken : issued
    User "1" --> "*" RestaurantStaff : works at
    Restaurant "1" --> "*" RestaurantStaff : employs
    Restaurant "1" --> "*" RestaurantHours : opens
```

**Nullable:** `RefreshToken.UsedAt`, `RefreshToken.RevokedAt`, `PasswordResetToken.UsedAt`,
`Restaurant.Description`, `Restaurant.LogoUrl`.

**Notes.**
`RefreshToken.FamilyId` groups every token descended from one login. Reuse of a token that
already has `UsedAt` set revokes the whole family — that is the theft detection in ADR-10.
`PlatformSetting` is a key/value table holding the default commission and the stale-order
threshold; it is the home those values currently lack.

---

## 2. Geography and addresses

```mermaid
classDiagram
    class DeliveryZone {
        Guid Id
        string Name
        bool IsActive
    }
    class RestaurantZone {
        Guid RestaurantId
        Guid ZoneId
        decimal DeliveryFeeUsd
        int EstimatedMinutes
        bool IsActive
    }
    class Address {
        Guid Id
        Guid UserId
        string Label
        Guid ZoneId
        string Line1
        string Building
        string Floor
        string Landmark
        decimal Lat
        decimal Lng
        bool IsDefault
    }
    class Restaurant
    class User

    Restaurant "1" --> "*" RestaurantZone : delivers to
    DeliveryZone "1" --> "*" RestaurantZone : served by
    DeliveryZone "1" --> "*" Address : contains
    User "1" --> "*" Address : saves
```

**Nullable:** `Address.Building`, `Address.Floor`, `Address.Landmark`, `Address.Lat`, `Address.Lng`.

**Notes.**
`DeliveryZone` is platform-level and shared. `RestaurantZone` is where fee and coverage live,
so two restaurants can charge different fees into the same zone. Lebanon has no reliable street
addressing, so `Landmark` carries real weight and coordinates are optional.

---

## 3. Menu and options

```mermaid
classDiagram
    class Category {
        Guid Id
        Guid RestaurantId
        string Name
        int SortOrder
        bool IsActive
    }
    class MenuItem {
        Guid Id
        Guid RestaurantId
        Guid CategoryId
        string Name
        string Description
        decimal BasePriceUsd
        string ImageUrl
        bool IsAvailable
        int SortOrder
        bool IsDeleted
    }
    class OptionGroup {
        Guid Id
        Guid RestaurantId
        string Name
        int MinSelect
        int MaxSelect
        int SortOrder
    }
    class Option {
        Guid Id
        Guid OptionGroupId
        string Name
        decimal PriceDeltaUsd
        int MaxQuantity
        bool IsAvailable
        int SortOrder
    }
    class MenuItemOptionGroup {
        Guid MenuItemId
        Guid OptionGroupId
        int SortOrder
        int MinSelectOverride
        int MaxSelectOverride
    }

    Category "1" --> "*" MenuItem : groups
    MenuItem "1" --> "*" MenuItemOptionGroup : attaches
    OptionGroup "1" --> "*" MenuItemOptionGroup : attached to
    OptionGroup "1" --> "*" Option : offers
```

**Nullable:** `MenuItem.Description`, `MenuItem.ImageUrl`, `OptionGroup.MaxSelect` (null = unlimited),
`MenuItemOptionGroup.MinSelectOverride`, `MenuItemOptionGroup.MaxSelectOverride`.

**Notes.**
`MenuItemOptionGroup` is the many-to-many that lets one "Extras" group serve every burger.
The two override columns answer the per-item limit problem: null inherits the group's value,
a number applies to that item alone. Effective limit is `override ?? group`.

`MenuItem.IsDeleted` is a soft delete — a hard delete would orphan historical order lines.

---

## 4. Cart

```mermaid
classDiagram
    class Cart {
        Guid Id
        Guid UserId
        Guid RestaurantId
        DateTime UpdatedAt
    }
    class CartLine {
        Guid Id
        Guid CartId
        Guid MenuItemId
        int Quantity
        string Note
    }
    class CartLineOption {
        Guid CartLineId
        Guid OptionId
        int Quantity
    }

    Cart "1" --> "*" CartLine : holds
    CartLine "1" --> "*" CartLineOption : selects
```

**Nullable:** `CartLine.Note`.

**Notes.**
One cart per user per restaurant. The cart references *live* menu rows and carries no prices —
prices are read fresh on every view and only frozen at checkout. This is deliberate: a cart that
stored prices would let a customer hold a stale price indefinitely.

---

## 5. Orders, payment and money

```mermaid
classDiagram
    class Order {
        Guid Id
        string OrderNumber
        Guid CustomerId
        Guid RestaurantId
        Guid AddressId
        FulfillmentType FulfillmentType
        OrderStatus Status
        PaymentMethod PaymentMethod
        PaymentStatus PaymentStatus
        decimal SubtotalUsd
        decimal DeliveryFeeUsd
        decimal TaxUsd
        decimal DiscountUsd
        decimal TotalUsd
        decimal ExchangeRateLbp
        decimal CommissionPercent
        decimal CommissionUsd
        int PromisedMinutesMin
        int PromisedMinutesMax
        string CustomerNote
        string RejectionReason
        Guid IdempotencyKey
        byte[] RowVersion
        DateTime PlacedAt
    }
    class OrderLine {
        Guid Id
        Guid OrderId
        Guid MenuItemId
        string ItemNameSnapshot
        decimal UnitPriceUsd
        int Quantity
        decimal LineTotalUsd
        string Note
    }
    class OrderLineOption {
        Guid Id
        Guid OrderLineId
        Guid OptionId
        string GroupNameSnapshot
        string OptionNameSnapshot
        decimal PriceDeltaUsd
        int Quantity
    }
    class OrderEvent {
        Guid Id
        Guid OrderId
        OrderStatus FromStatus
        OrderStatus ToStatus
        Guid ChangedByUserId
        string Note
        DateTime CreatedAt
    }
    class Payment {
        Guid Id
        Guid OrderId
        PaymentMethod Method
        decimal AmountUsd
        PaymentStatus Status
        string ProviderRef
        DateTime CreatedAt
    }
    class ExchangeRate {
        Guid Id
        decimal RateLbpPerUsd
        DateTime EffectiveFrom
        Guid SetByUserId
    }

    Order "1" --> "*" OrderLine : contains
    OrderLine "1" --> "*" OrderLineOption : selected
    Order "1" --> "*" OrderEvent : logs
    Order "1" --> "*" Payment : settled by
```

**Nullable:** `Order.AddressId` (null for pickup), `Order.CustomerNote`, `Order.RejectionReason`,
`OrderLine.MenuItemId`, `OrderLine.Note`, `OrderLineOption.OptionId`, `OrderEvent.Note`,
`Payment.ProviderRef`.

**Notes.**

*The snapshot rule.* `OrderLine` and `OrderLineOption` keep the name and price as sold, and their
FKs to `MenuItem` and `Option` are nullable and used only for reporting. When a restaurant renames
a burger or raises its price, last month's orders stay exactly as they were sold.

*`CommissionPercent` is snapshotted too*, so changing a restaurant's rate never rewrites historical
settlement figures.

*`TaxUsd` stays at 0.* No tax was decided this round; the column remains so that turning VAT on
later is a config change rather than a migration plus a backfill of every historical total.

*`IdempotencyKey` carries a unique index.* A double-tap on a bad connection returns the original
order instead of creating a second one.

*`RowVersion` is the concurrency token.* Two staff pressing Accept on two tablets: the second write
fails and the UI refreshes, instead of both succeeding.

---

## 6. Why one multi-tenant platform, not a copy sold per restaurant

This is the fork worth being deliberate about, because the two paths are different businesses,
not just different code.

```mermaid
flowchart TB
    subgraph MT["Option A — one platform, many restaurants"]
        direction LR
        C1["Customer"] -->|browses all| API1["One API"]
        S1["Restaurant A staff"] -->|token carries RestaurantId| API1
        S2["Restaurant B staff"] -->|token carries RestaurantId| API1
        API1 -->|filtered by RestaurantId| DB1[("One database")]
    end

    subgraph ST["Option B — one copy sold per restaurant"]
        direction LR
        C2["Customer of A"] --> API2["App copy A"] --> DB2[("Database A")]
        C3["Customer of B"] --> API3["App copy B"] --> DB3[("Database B")]
        C4["Customer of C"] --> API4["App copy C"] --> DB4[("Database C")]
    end
```

You are right that Option B gets isolation for free — restaurant A physically cannot reach
restaurant B's database, so no query filter can be forgotten. That is a genuine advantage and the
strongest argument for it.

Three things outweigh it.

**1. Multi-tenant is a superset. Single-tenant is a dead end.**
A multi-tenant system can be sold as a single-restaurant product by onboarding exactly one tenant —
same code, one row in `Restaurant`. Going the other way is a rewrite: adding `RestaurantId` to every
table, backfilling it, and reworking every query. Option A keeps both business models available;
Option B closes one permanently.

**2. Option B deletes about a third of the spec.**
Commission and two-directional settlement (spec §8) only exist when a platform sits between customer
and restaurant. So does the `PlatformAdmin` role, the shared `DeliveryZone` table, and cross-restaurant
browsing. If a customer of restaurant A is a different account from a customer of restaurant B, the
whole marketplace premise goes with it — including the part of the spec that is most worth showing.

**3. Operational cost scales linearly and eats the margin.**
Ten clients on Option B means ten databases to migrate on every release, ten deployments to keep in
step, and ten versions that drift the moment one client delays an upgrade. On Option A a release is
one migration and one deploy regardless of how many restaurants are on it.

**What Option A costs, stated honestly.** Isolation becomes a code property rather than a physical
one, and a bug in it is a cross-tenant data leak — the worst failure this system can have. That is
exactly why ADR-07 spends three layers on it (query filters, explicit write guards, and tests that
assert 403) instead of trusting any one of them.

**The escape hatch stays open.** If a client ever demands their own database — for a contract, or
because they are large enough to want it — you run a second instance of the same code with one
tenant in it. Nothing needs rewriting. That option only exists on Option A.
