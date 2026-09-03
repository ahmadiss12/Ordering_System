import { Route } from '@angular/router';
import { MaterialIcon } from 'material-icons';
import { RESTAURANT_OWNER, RESTAURANT_STAFF, Role, roleGuard } from 'auth';

/**
 * One section of the dashboard: a route, and the sidenav entry that leads to it.
 */
export interface NavItem {
  /** Route path, relative to the shell. Empty string is the landing section. */
  readonly path: string;
  readonly label: string;
  /**
   * Material icon ligature.
   *
   * Typed against the bundled font's own list rather than as a string. A name that font does not
   * carry — `skillet`, say, which exists in Material Symbols but not here — renders as nothing at
   * all, with no error anywhere; this turns that into a build failure. Templates cannot be
   * checked this way, which is what icons.spec.ts is for.
   */
  readonly icon: MaterialIcon;
  /** Roles allowed in. Omitted means any signed-in user. */
  readonly roles?: readonly Role[];
}

/**
 * The sections, in the order they appear in the sidenav.
 *
 * Roles are declared once, here, and read by two places: {@link navChild} turns them into the
 * route's guard, and the shell uses them to decide what to draw. That is deliberate — keeping
 * the list and the guards in separate places is how an app ends up showing a menu entry that
 * bounces you to /forbidden when you click it, which reads as broken rather than as forbidden.
 *
 * These mirror the API's authorization policies (`AuthorizationPolicies.cs`). The server is
 * still the one enforcing them; this only decides what is worth putting on screen.
 */
export const NAV = {
  overview: {
    path: '',
    label: 'Overview',
    icon: 'dashboard',
  },
  orders: {
    path: 'orders',
    label: 'Queue',
    icon: 'receipt_long',
    roles: RESTAURANT_STAFF,
  },
  history: {
    path: 'history',
    label: 'History',
    icon: 'history',
    roles: RESTAURANT_STAFF,
  },
  menu: {
    path: 'menu',
    label: 'Menu',
    icon: 'restaurant_menu',
    roles: RESTAURANT_STAFF,
  },
  settings: {
    path: 'settings',
    label: 'Settings',
    icon: 'settings',
    roles: RESTAURANT_OWNER,
  },
} as const satisfies Record<string, NavItem>;

export const NAV_ITEMS: readonly NavItem[] = Object.values(NAV);

/**
 * Builds a child route for a section, attaching the guard its roles imply.
 *
 * Pairing them in one call is what makes the guarantee above hold: there is no way to add a
 * section to the sidenav without also guarding its route.
 */
export function navChild(item: NavItem, loadComponent: Route['loadComponent']): Route {
  return {
    path: item.path,
    loadComponent,
    ...(item.roles ? { canActivate: [roleGuard(...item.roles)] } : {}),
  };
}
