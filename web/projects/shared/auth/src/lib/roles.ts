/**
 * The role names that appear in the `role` claim.
 *
 * These strings are `RoleType` member names, serialised by the token issuer with `nameof`. They
 * are duplicated here because no DTO references the enum, so it never reaches the generated
 * client — see `api/src/OrderingSystem.Domain/Enums/RoleType.cs`. A test on the API side asserts
 * this list still matches, so a rename over there fails the build rather than silently emptying
 * a navigation menu over here.
 */
export const Roles = {
  Customer: 'Customer',
  RestaurantStaff: 'RestaurantStaff',
  RestaurantOwner: 'RestaurantOwner',
  PlatformAdmin: 'PlatformAdmin',
} as const;

export type Role = (typeof Roles)[keyof typeof Roles];

/**
 * Anyone acting for a restaurant. Mirrors the API's `RestaurantStaff` policy, which admits an
 * owner as well as a staff member — an owner who could not open the menu editor would be an
 * odd kind of owner.
 */
export const RESTAURANT_STAFF: readonly Role[] = [Roles.RestaurantStaff, Roles.RestaurantOwner];

/** Owner-only areas: staff accounts, delivery zones, fees, prep time. */
export const RESTAURANT_OWNER: readonly Role[] = [Roles.RestaurantOwner];

/**
 * The platform's own areas: commission and the listing switch.
 *
 * Deliberately not including RestaurantOwner. An owner is the top of their restaurant and the
 * bottom of this list — what they are charged, and whether they are listed at all, are not theirs
 * to set.
 */
export const PLATFORM_ADMIN: readonly Role[] = [Roles.PlatformAdmin];
