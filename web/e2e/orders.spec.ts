import { expect, test } from '@playwright/test';
import {
  ACCOUNTS,
  API,
  ALWAYS_OPEN_SLUG,
  attemptOrder,
  checkout,
  placeOrder,
  signIn,
  stockBasket,
  tokenFor,
} from './helpers';

/**
 * An order's whole life, through the real stack.
 *
 * This is the test the phase was built for. Everything else proves a piece: the domain tests prove
 * the transition table, the integration tests prove the endpoints, the component tests prove the
 * board. Only this one proves they are joined — that an order placed by a customer's app appears
 * on a kitchen screen nobody touched, that pressing the buttons moves it, and that the trail
 * afterwards says who did what.
 *
 * Orders are placed through the API rather than a screen because the storefront is phase 5. The
 * subject is Shawarma Station, the one seeded restaurant that never closes: the checkout refuses
 * an order to a shut kitchen, correctly, and a browser test has no clock it can move.
 */
test.describe('an order from placed to delivered', () => {
  test('a new order reaches the kitchen with nobody pressing anything', async ({
    page,
    request,
  }) => {
    await signIn(page, ACCOUNTS.shawarma);
    await page.goto('/orders');
    await expect(page.getByRole('heading', { name: 'Queue' })).toBeVisible();

    const order = await placeOrder(request);

    // Generous, and deliberately so. A socket delivers this in milliseconds; if it has dropped,
    // the poll behind it takes up to ten seconds. Both are the product working — what would be a
    // failure is the order never arriving at all.
    await expect(page.getByRole('button', { name: order.orderNumber })).toBeVisible({
      timeout: 20_000,
    });
  });

  test('the kitchen walks it to delivered, one press at a time', async ({ page, request }) => {
    const order = await placeOrder(request);

    await signIn(page, ACCOUNTS.shawarma);
    await page.goto('/orders');

    const card = page.locator('.order', { hasText: order.orderNumber });
    await expect(card).toBeVisible({ timeout: 20_000 });

    // Each press is the only one offered on that card, because the buttons come from the
    // transition table rather than from anything this screen decided.
    for (const action of ['Accept', 'Start cooking', 'Ready', 'Collected']) {
      await page
        .locator('.order', { hasText: order.orderNumber })
        .getByRole('button', { name: action })
        .click();
    }

    // Delivered is terminal, so it leaves the live board entirely.
    await expect(page.locator('.order', { hasText: order.orderNumber })).toHaveCount(0, {
      timeout: 20_000,
    });

    const detail = await orderDetail(request, order.id);
    expect(detail.status, 'Delivered').toBe(6);

    // The point of the trail: every step, in order, with the account that made it.
    expect(detail.events.map((e) => e.toStatus)).toEqual([1, 2, 3, 4, 6]);
    expect(detail.events[0].changedBy).toBe('Rita Customer');
    expect(detail.events.slice(1).map((e) => e.changedBy)).toEqual([
      'Layla Owner',
      'Layla Owner',
      'Layla Owner',
      'Layla Owner',
    ]);
  });

  test('refusing an order records the reason it was given', async ({ page, request }) => {
    const order = await placeOrder(request);

    await signIn(page, ACCOUNTS.shawarma);
    await page.goto('/orders');

    const card = page.locator('.order', { hasText: order.orderNumber });
    await expect(card).toBeVisible({ timeout: 20_000 });
    await card.getByRole('button', { name: 'Refuse' }).click();

    // The state machine will not take a rejection without one, so the screen has to ask.
    await expect(page.getByRole('heading', { name: 'Refuse this order?' })).toBeVisible();
    await page.getByRole('radio', { name: "We're too busy right now" }).check();
    await page.getByRole('button', { name: 'Refuse order' }).click();

    await expect(page.locator('.order', { hasText: order.orderNumber })).toHaveCount(0, {
      timeout: 20_000,
    });

    // And it reaches the history, in the words a person chose rather than as an enum name.
    await page.goto('/history');
    const row = page.locator('.row', { hasText: order.orderNumber });
    await expect(row).toBeVisible();
    await expect(row).toContainText("We're too busy right now");

    // The receipt says the same, and the trail names who refused it.
    await row.getByRole('button', { name: order.orderNumber }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toContainText("We're too busy right now");
    await expect(dialog).toContainText('Layla Owner');
  });
});

/**
 * The refusals a customer can actually run into.
 *
 * A closed restaurant is deliberately not here. It is a genuine refusal and it is tested — in the
 * integration suite, which runs on a clock it can move. Recreating it in a browser would mean
 * either waiting for a particular hour or asserting nothing for most of the day.
 */
test.describe('orders that are refused', () => {
  test('a basket under the minimum says how much more is needed', async ({ request }) => {
    // Shawarma Station's minimum is $6 and its cheapest wrap is $4.50.
    const refused = await attemptOrder(request, { itemName: 'Chicken Shawarma Wrap', quantity: 1 });

    expect(refused.status).toBe(409);
    expect(refused.body).toContain('minimum order');
    // Naming the shortfall is the difference between a customer fixing it in one tap and giving up.
    expect(refused.body).toMatch(/Add \$\d+\.\d\d more/);
  });

  test('a dish that sold out while the basket sat is named', async ({ request }) => {
    const token = await tokenFor(request, ACCOUNTS.shawarma);
    const auth = { Authorization: `Bearer ${token}` };

    const items = (await (
      await request.get(`${API}/api/restaurant/menu-items`, { headers: auth })
    ).json()) as { id: string; name: string }[];

    const wrap = items.find((i) => i.name === 'Chicken Shawarma Wrap');
    expect(wrap, 'the wrap should be on the menu').toBeTruthy();

    // The basket is filled while the wrap is still on: this refusal only exists in the gap
    // between deciding and paying. Adding an item that is already off is refused at the basket,
    // which the first version of this test discovered by getting "your basket is empty" instead.
    const basket = await stockBasket(request, { itemName: 'Chicken Shawarma Wrap', quantity: 2 });

    try {
      await request.patch(`${API}/api/restaurant/menu-items/${wrap!.id}/availability`, {
        headers: auth,
        data: { isAvailable: false },
      });

      const refused = await checkout(request, basket);

      expect(refused.status).toBe(409);
      // By name. "Something in your basket is unavailable" leaves a customer hunting.
      expect(refused.body).toContain('Chicken Shawarma Wrap');
    } finally {
      await request.patch(`${API}/api/restaurant/menu-items/${wrap!.id}/availability`, {
        headers: auth,
        data: { isAvailable: true },
      });
    }
  });

  test('the public menu shows the always-open kitchen as open', async ({ request }) => {
    // Not incidental: every order test above depends on it, and a seed change that closed this
    // restaurant would otherwise fail those tests with an error about baskets.
    const list = (await (await request.get(`${API}/api/restaurants`)).json()) as {
      items: { slug: string; isOpenNow: boolean }[];
    };

    const shawarma = list.items.find((r) => r.slug === ALWAYS_OPEN_SLUG);
    expect(shawarma?.isOpenNow, `${ALWAYS_OPEN_SLUG} must never close`).toBe(true);
  });
});

interface OrderDetail {
  readonly status: number;
  readonly events: { toStatus: number; changedBy: string | null }[];
}

async function orderDetail(
  request: Parameters<typeof tokenFor>[0],
  orderId: string,
): Promise<OrderDetail> {
  const token = await tokenFor(request, ACCOUNTS.shawarma);
  const response = await request.get(`${API}/api/orders/${orderId}`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  expect(response.ok(), `could not read order ${orderId}`).toBeTruthy();
  return (await response.json()) as OrderDetail;
}
