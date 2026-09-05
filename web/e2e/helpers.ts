import { APIRequestContext, Page, expect } from '@playwright/test';

export const API = process.env['E2E_API_URL'] ?? 'http://localhost:5248';

/** Seeded accounts, from DatabaseSeeder. */
export const ACCOUNTS = {
  owner: { email: 'owner@frieslab.test', password: 'Passw0rd!' },
  staff: { email: 'staff@frieslab.test', password: 'Passw0rd!' },
  /** Shawarma Station's owner — the restaurant the order tests use. See ALWAYS_OPEN_SLUG. */
  shawarma: { email: 'owner@shawarma.test', password: 'Passw0rd!' },
  customer: { email: 'rita@example.test', password: 'Passw0rd!' },
} as const;

export const RESTAURANT_SLUG = 'frieslab';

/**
 * The one seeded restaurant that never closes.
 *
 * The order tests place real orders against a real server, and the checkout refuses an order to a
 * restaurant that is shut — correctly. There is no clock a browser test can move, so the subject
 * has to be a kitchen with no closing time, or the whole suite would pass or fail depending on
 * the hour it happened to run. FriesLab keeps its noon-to-two window, which two integration tests
 * rely on precisely because they *can* move the clock.
 */
export const ALWAYS_OPEN_SLUG = 'shawarma-station';

/** Signs in through the real form, so the interceptor and guards are exercised too. */
export async function signIn(page: Page, account = ACCOUNTS.owner): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Email').fill(account.email);
  await page.getByLabel('Password').fill(account.password);
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page).toHaveURL(/\/$|\/menu/);
}

/**
 * The public menu, read straight from the API with no token.
 *
 * This is what makes the headline test end-to-end rather than a tour of the dashboard: it checks
 * that a dish typed into the editor is visible to a customer who has never signed in.
 */
export async function publicMenu(request: APIRequestContext): Promise<PublicMenu> {
  const response = await request.get(`${API}/api/restaurants/${RESTAURANT_SLUG}/menu`);
  expect(response.ok(), 'the public menu should be readable without signing in').toBeTruthy();
  return (await response.json()) as PublicMenu;
}

export interface PublicMenu {
  readonly categories: readonly {
    readonly name: string;
    readonly items: readonly { readonly name: string; readonly basePriceUsd: number }[];
  }[];
}

/** Every item name on the public menu, flattened. */
export function publicItemNames(menu: PublicMenu): string[] {
  return menu.categories.flatMap((c) => c.items.map((i) => i.name));
}

/** A name no other run will collide with, since these tests share one database. */
export function uniqueName(prefix: string): string {
  return `${prefix} ${Date.now().toString().slice(-6)}`;
}

// ---------------------------------------------------------------- ordering, through the API

/** A bearer token for a seeded account. */
export async function tokenFor(
  request: APIRequestContext,
  account: { email: string; password: string },
): Promise<string> {
  const response = await request.post(`${API}/api/auth/login`, { data: account });
  expect(response.ok(), `could not sign in as ${account.email}`).toBeTruthy();

  return ((await response.json()) as { accessToken: string }).accessToken;
}

export interface PlacedOrder {
  readonly id: string;
  readonly orderNumber: string;
  readonly totalUsd: number;
}

/** A stocked basket, ready to be checked out — or to have the world change underneath it. */
export interface Basket {
  readonly restaurantId: string;
  readonly headers: Record<string, string>;
  readonly quotedTotalUsd: number;
}

/**
 * Fills a customer's basket at the always-open restaurant and quotes it.
 *
 * Split from checking out on purpose. Some of the refusals only exist in the gap between the two
 * — a dish selling out while somebody decides is the whole point of that check, and it cannot be
 * reached by a helper that does both in one call, because adding an unavailable item is refused
 * at the basket already.
 */
export async function stockBasket(
  request: APIRequestContext,
  options: { itemName?: string; quantity?: number } = {},
): Promise<Basket> {
  const token = await tokenFor(request, ACCOUNTS.customer);
  const headers = { Authorization: `Bearer ${token}` };

  const restaurant = await restaurantId(request);
  const item = await menuItem(request, options.itemName);

  await request.delete(`${API}/api/restaurants/${restaurant}/cart`, { headers });

  const added = await request.post(`${API}/api/restaurants/${restaurant}/cart/lines`, {
    headers,
    data: {
      menuItemId: item.id,
      quantity: options.quantity ?? 2,
      note: null,
      options: item.choices,
    },
  });
  expect(added.ok(), `could not add ${item.name}: ${await added.text()}`).toBeTruthy();

  const quote = (await (
    await request.get(`${API}/api/restaurants/${restaurant}/cart/quote?fulfillment=Pickup`, {
      headers,
    })
  ).json()) as { totalUsd: number };

  return { restaurantId: restaurant, headers, quotedTotalUsd: quote.totalUsd };
}

/** Checks a basket out, handing back whatever the API said — success or refusal. */
export async function checkout(
  request: APIRequestContext,
  basket: Basket,
): Promise<{ status: number; body: string }> {
  const response = await request.post(`${API}/api/restaurants/${basket.restaurantId}/orders`, {
    headers: basket.headers,
    data: {
      fulfillment: FULFILLMENT_PICKUP,
      addressId: null,
      paymentMethod: PAYMENT_CASH,
      customerNote: null,
      expectedTotalUsd: basket.quotedTotalUsd,
      idempotencyKey: crypto.randomUUID(),
    },
  });

  return { status: response.status(), body: await response.text() };
}

/**
 * Places an order the way a customer's app would: basket, quote, checkout.
 *
 * Through the API rather than through a screen because the storefront does not exist yet — that
 * is phase 5. What matters here is that an order arriving from outside the dashboard shows up
 * inside it.
 */
export async function placeOrder(
  request: APIRequestContext,
  options: { itemName?: string; quantity?: number } = {},
): Promise<PlacedOrder> {
  const basket = await stockBasket(request, options);
  const result = await checkout(request, basket);

  expect(result.status, `checkout failed: ${result.body}`).toBe(201);
  return JSON.parse(result.body) as PlacedOrder;
}

/** Tries to check out a basket and hands back the refusal, for the tests about being refused. */
export async function attemptOrder(
  request: APIRequestContext,
  options: { itemName?: string; quantity: number },
): Promise<{ status: number; body: string }> {
  const basket = await stockBasket(request, options);
  const result = await checkout(request, basket);

  await request.delete(`${API}/api/restaurants/${basket.restaurantId}/cart`, {
    headers: basket.headers,
  });

  return result;
}

/** FulfillmentType.Pickup and PaymentMethod.CashOnDelivery, as the enums number them. */
const FULFILLMENT_PICKUP = 2;
const PAYMENT_CASH = 1;

async function restaurantId(request: APIRequestContext): Promise<string> {
  const list = (await (await request.get(`${API}/api/restaurants`)).json()) as {
    items: { id: string; slug: string }[];
  };

  const found = list.items.find((r) => r.slug === ALWAYS_OPEN_SLUG);
  expect(found, `${ALWAYS_OPEN_SLUG} should be in the seed`).toBeTruthy();

  return found!.id;
}

/**
 * A menu item and the choices it demands, so a caller does not have to know which dishes carry a
 * required option group.
 */
async function menuItem(
  request: APIRequestContext,
  name?: string,
): Promise<{ id: string; name: string; choices: { optionId: string; quantity: number }[] }> {
  const menu = (await (
    await request.get(`${API}/api/restaurants/${ALWAYS_OPEN_SLUG}/menu`)
  ).json()) as {
    categories: { items: { id: string; name: string; basePriceUsd: number }[] }[];
  };

  const items = menu.categories.flatMap((c) => c.items);
  const chosen = name ? items.find((i) => i.name === name) : items[0];
  expect(chosen, `${name ?? 'any item'} should be on the ${ALWAYS_OPEN_SLUG} menu`).toBeTruthy();

  const detail = (await (await request.get(`${API}/api/menu-items/${chosen!.id}`)).json()) as {
    optionGroups: { minSelect: number; options: { id: string }[] }[];
  };

  // One from each group that demands a choice. Anything less and the cart refuses the line for a
  // reason that has nothing to do with what the test is about.
  const choices = detail.optionGroups
    .filter((g) => g.minSelect > 0)
    .map((g) => ({ optionId: g.options[0].id, quantity: 1 }));

  return { id: chosen!.id, name: chosen!.name, choices };
}
