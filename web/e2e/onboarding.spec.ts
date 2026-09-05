import { APIRequestContext, expect, test } from '@playwright/test';
import {
  ACCOUNTS,
  API,
  FRESH_SLUG,
  placeOrder,
  signIn,
  signOut,
  tokenFor,
  uniqueName,
} from './helpers';

/**
 * A restaurant from nothing to taking orders, through the product alone.
 *
 * <p>
 * The test this phase exists to pass. Everything else proves one screen; this proves the screens
 * add up to something a real restaurant could be onboarded with — the platform lists it, its
 * owner sets the hours, the delivery area, the fee and the menu, hires somebody, and then a
 * customer orders and the kitchen cooks it.
 * </p>
 *
 * <p>
 * The subject is Saj Corner, seeded with nothing and hidden. It starts by being put back to that
 * state, because the journey configures it and a second run would otherwise begin halfway
 * through — proving that a configured restaurant stays configured, which is not the claim.
 * </p>
 */
/** What the seeder gives Saj Corner, and what the reset puts back. */
const SEEDED_COMMISSION_PERCENT = 15;

/** What the platform negotiates it down to during the journey. */
const AGREED_COMMISSION_PERCENT = 12;

/** The delivery fee the owner sets, distinctive enough to recognise coming back. */
const AGREED_DELIVERY_FEE_USD = 3.75;

test.describe('onboarding a restaurant', () => {
  test('goes from nothing to a cooked order without leaving the product', async ({
    page,
    request,
  }) => {
    await resetFreshRestaurant(request);

    const dish = uniqueName('Manakish');
    const hire = `hire-${Date.now().toString().slice(-8)}@example.test`;

    await test.step('the platform lists it and sets what it will charge', async () => {
      await signIn(page, ACCOUNTS.admin);
      await page.goto('/platform');

      const row = page.locator('.restaurant', { hasText: 'Saj Corner' });
      await expect(row).toBeVisible();
      await expect(row.locator('.badge.hidden')).toBeVisible();

      await row.getByRole('button', { name: /list again/i }).click();
      await expect(row.locator('.badge.hidden')).toHaveCount(0);

      await expect(row.locator('input[type=number]')).toHaveValue(`${SEEDED_COMMISSION_PERCENT}`);

      await row.locator('input[type=number]').fill(`${AGREED_COMMISSION_PERCENT}`);
      await row.getByRole('button', { name: /^save$/i }).click();
      await page.getByRole('button', { name: /change the rate/i }).click();

      await expect(row.locator('input[type=number]')).toHaveValue(
        `${AGREED_COMMISSION_PERCENT}`,
      );
    });

    // Handed over through the product: the guard that keeps a signed-in visitor off /login is
    // real, and swapping accounts behind its back would skip the part where somebody logs out.
    await signOut(page);
    await signIn(page, ACCOUNTS.freshOwner);

    await test.step('the owner says when the kitchen is open', async () => {
      await page.goto('/settings');

      // Every day, around the clock. Not 00:00 to 23:59, which is a different promise and is what
      // the form used to force — this control exists because the closing time that means "does
      // not close" cannot be typed into an input.
      const monday = page.locator('.day').first();
      await monday.getByRole('button', { name: 'All day' }).click();
      await page.getByRole('button', { name: /copy monday to every day/i }).click();
      await page.getByRole('button', { name: /save hours/i }).click();

      await expect(page.getByText('Open now')).toBeVisible({ timeout: 10_000 });
    });

    await test.step('and where it delivers, and for how much', async () => {
      const zone = page.locator('app-zones .zone').first();
      const zoneName = (await zone.locator('.name').textContent())!.trim();

      await zone.getByRole('switch').click();
      await zone.locator('input[type=number]').first().fill(`${AGREED_DELIVERY_FEE_USD}`);
      await zone.getByRole('button', { name: /^save$/i }).click();

      await expect(zone.getByRole('button', { name: /^save$/i })).toHaveCount(0, {
        timeout: 10_000,
      });

      // Reloaded, so this is the server's answer rather than the form's memory of what was typed.
      await page.reload();

      const saved = page.locator('app-zones .zone', { hasText: zoneName });
      await expect(saved.getByRole('switch')).toBeChecked({ timeout: 10_000 });
      await expect(saved.locator('input[type=number]').first()).toHaveValue(
        `${AGREED_DELIVERY_FEE_USD}`,
      );

      // Following that fee through to what a customer is charged needs an address, and there is
      // no endpoint yet for a customer to have one — addresses arrive with the storefront in
      // phase 5. Until then the order below is a pickup, and the integration suite proves the
      // other half against the database: a fee change moves the next quote and no placed order.
    });

    await test.step('and hires somebody to work the queue', async () => {
      const staff = page.locator('app-staff');

      await staff.getByLabel('Email').fill(hire);
      await staff.getByLabel('Name').fill('New Cook');
      await staff.getByRole('button', { name: /send invitation/i }).click();

      await expect(staff.locator('.person', { hasText: hire })).toBeVisible({ timeout: 10_000 });
      await expect(staff.locator('.person', { hasText: hire }).locator('.waiting')).toBeVisible();
    });

    await test.step('and puts one dish on the menu', async () => {
      await page.goto('/menu');

      const section = uniqueName('Saj');

      await page.getByRole('button', { name: 'Add section' }).click();
      await page.getByLabel('Section name').fill(section);
      await page.getByRole('button', { name: 'Add', exact: true }).click();

      const card = page.locator('.section').filter({ hasText: section });
      await expect(card).toBeVisible({ timeout: 10_000 });

      await card.getByRole('button', { name: 'Add item' }).click();

      const dialog = page.getByRole('dialog');
      await dialog.getByLabel('Name').fill(dish);
      await dialog.getByLabel('Price').fill('6.50');
      await dialog.getByRole('button', { name: 'Add item' }).click();

      await expect(page.locator('.item').filter({ hasText: dish })).toBeVisible({
        timeout: 10_000,
      });
    });

    await test.step('a customer who has never seen this restaurant can order from it', async () => {
      // Through the API, because the storefront is phase 5 — but against the public catalog,
      // which is what a customer's app would read. Nothing here uses a restaurant token.
      const response = await request.get(`${API}/api/restaurants/${FRESH_SLUG}/menu`);
      expect(
        response.ok(),
        'a customer should be able to read the menu of a restaurant the platform has listed',
      ).toBeTruthy();

      const menu = (await response.json()) as { categories: { items: { name: string }[] }[] };
      const names = menu.categories.flatMap((c) => c.items.map((i) => i.name));

      expect(names, 'the dish just typed in should be on the public menu').toContain(dish);

      const order = await placeOrder(request, { slug: FRESH_SLUG, itemName: dish, quantity: 2 });
      expect(order.orderNumber).toContain('SAJCORNER');
    });

    await test.step('and the kitchen cooks it', async () => {
      await page.goto('/orders');

      const number = await firstQueuedOrderNumber(page);
      const card = page.locator('.order', { hasText: number });
      await expect(card).toBeVisible({ timeout: 20_000 });

      for (const action of ['Accept', 'Start cooking', 'Ready', 'Collected']) {
        await page.locator('.order', { hasText: number }).getByRole('button', { name: action }).click();
      }

      // Delivered is terminal, so it leaves the live board — the restaurant has completed an
      // order, which is the whole claim.
      await expect(page.locator('.order', { hasText: number })).toHaveCount(0, { timeout: 20_000 });
    });

    await test.step('and the day shows up in its report', async () => {
      await page.goto('/reports');

      const revenue = page.locator('.tile', { hasText: 'Revenue' }).locator('.figure');
      await expect(revenue).not.toHaveText('$0.00', { timeout: 10_000 });
    });

    await removeHire(request, hire);
  });
});

/** The order at the top of the queue, which is the one just placed — the board is oldest first. */
async function firstQueuedOrderNumber(page: import('@playwright/test').Page): Promise<string> {
  const first = page.locator('.order .number').first();
  await expect(first).toBeVisible({ timeout: 20_000 });

  return (await first.textContent())!.trim();
}

/**
 * Puts Saj Corner back to the state the seeder leaves it in.
 *
 * Through the API rather than the UI: this is scaffolding, not the journey. The menu is left
 * alone — every run adds a uniquely named dish and orders that one, so leftovers are harmless,
 * and deleting a menu somebody's earlier order refers to is not something the product allows for
 * good reason.
 */
async function resetFreshRestaurant(request: APIRequestContext): Promise<void> {
  const adminHeaders = { Authorization: `Bearer ${await tokenFor(request, ACCOUNTS.admin)}` };
  const ownerHeaders = { Authorization: `Bearer ${await tokenFor(request, ACCOUNTS.freshOwner)}` };

  const platform = (await (
    await request.get(`${API}/api/platform/restaurants`, { headers: adminHeaders })
  ).json()) as { id: string; slug: string }[];

  const fresh = platform.find((r) => r.slug === FRESH_SLUG);
  expect(fresh, `${FRESH_SLUG} should be seeded`).toBeTruthy();

  // Emptying the week is a deliberate state, and the API insists it be confirmed as one.
  await request.put(`${API}/api/restaurant/hours`, {
    headers: ownerHeaders,
    data: { windows: [], confirmClosedIndefinitely: true },
  });

  const zones = (await (
    await request.get(`${API}/api/restaurant/zones`, { headers: ownerHeaders })
  ).json()) as { zoneId: string; isServed: boolean }[];

  for (const zone of zones.filter((z) => z.isServed)) {
    await request.put(`${API}/api/restaurant/zones/${zone.zoneId}`, {
      headers: ownerHeaders,
      data: { isServed: false, deliveryFeeUsd: 2, estimatedMinutes: 20 },
    });
  }

  // Anybody an earlier run hired and did not clear up, so the staff list starts as one owner.
  await removeEveryHire(request, ownerHeaders);

  // The rate as well as the listing. Without this the second run began already on the rate the
  // first one set, so typing it again left nothing to save and the step had no button to press —
  // a test that only passes the first time it is ever run is not a test.
  await request.put(`${API}/api/platform/restaurants/${fresh!.id}/commission`, {
    headers: adminHeaders,
    data: { commissionPercent: SEEDED_COMMISSION_PERCENT },
  });

  await request.put(`${API}/api/platform/restaurants/${fresh!.id}/listing`, {
    headers: adminHeaders,
    data: { isActive: false },
  });
}

async function removeHire(request: APIRequestContext, email: string): Promise<void> {
  const headers = { Authorization: `Bearer ${await tokenFor(request, ACCOUNTS.freshOwner)}` };
  const staff = (await (
    await request.get(`${API}/api/restaurant/staff`, { headers })
  ).json()) as { userId: string; email: string }[];

  const hired = staff.find((s) => s.email === email);
  if (hired) {
    await request.delete(`${API}/api/restaurant/staff/${hired.userId}`, { headers });
  }
}

async function removeEveryHire(
  request: APIRequestContext,
  headers: Record<string, string>,
): Promise<void> {
  const staff = (await (
    await request.get(`${API}/api/restaurant/staff`, { headers })
  ).json()) as { userId: string; email: string; isYou: boolean }[];

  for (const member of staff.filter((s) => !s.isYou)) {
    await request.delete(`${API}/api/restaurant/staff/${member.userId}`, { headers });
  }
}
