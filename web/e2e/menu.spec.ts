import { expect, test } from '@playwright/test';
import { publicItemNames, publicMenu, signIn, uniqueName } from './helpers';

test.describe('the menu editor', () => {
  test.beforeEach(async ({ page }) => {
    await signIn(page);
    await page.goto('/menu');
    await expect(page.getByRole('heading', { name: 'Menu' })).toBeVisible();
  });

  /**
   * The journey this whole phase was built for: a dish typed into the dashboard becomes a dish a
   * customer can order. Every other test checks one layer; this one checks that they are joined.
   */
  test('an item added here appears on the public menu', async ({ page, request }) => {
    const dish = uniqueName('E2E Burger');

    const before = publicItemNames(await publicMenu(request));
    expect(before).not.toContain(dish);

    await page
      .locator('.section')
      .filter({ hasText: 'Smashed Burgers' })
      .getByRole('button', { name: 'Add item' })
      .click();

    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Name').fill(dish);
    await dialog.getByLabel('Price').fill('11.25');
    await dialog.getByRole('button', { name: 'Add item' }).click();

    await expect(page.locator('.item').filter({ hasText: dish })).toBeVisible();

    // Read as a customer would: no token, straight from the public endpoint.
    const after = await publicMenu(request);
    expect(publicItemNames(after)).toContain(dish);

    const stored = after.categories.flatMap((c) => c.items).find((i) => i.name === dish);
    expect(stored?.basePriceUsd).toBe(11.25);

    await removeItem(page, dish);
    expect(publicItemNames(await publicMenu(request))).not.toContain(dish);
  });

  test('a sold-out item stays on the editor but is marked unavailable', async ({ page }) => {
    const dish = uniqueName('E2E Sold Out');

    await page
      .locator('.section')
      .filter({ hasText: 'Smashed Burgers' })
      .getByRole('button', { name: 'Add item' })
      .click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Name').fill(dish);
    await dialog.getByLabel('Price').fill('5');
    await dialog.getByRole('button', { name: 'Add item' }).click();
    await expect(page.locator('.item').filter({ hasText: dish })).toBeVisible();

    const row = page.locator('.item').filter({ hasText: dish });
    await row.getByRole('switch', { name: `Available: ${dish}` }).click();

    await page.reload();
    const afterReload = page.locator('.item').filter({ hasText: dish });

    // It has to still be listed. The person who switches it back on is looking at this list.
    await expect(afterReload).toBeVisible();
    await expect(afterReload.locator('.badge')).toHaveText('Sold out');

    await removeItem(page, dish);
  });

  test('a new section starts empty and can be hidden', async ({ page }) => {
    const section = uniqueName('E2E Section');

    await page.getByRole('button', { name: 'Add section' }).click();
    await page.getByLabel('Section name').fill(section);
    await page.getByRole('button', { name: 'Add', exact: true }).click();

    const card = page.locator('.section').filter({ hasText: section });
    await expect(card.getByText('0 items')).toBeVisible();

    await card.getByRole('switch').click();
    await page.reload();

    // Hidden is not deleted: it keeps its place and its items, which is what a seasonal menu
    // needs in the off season.
    await expect(
      page.locator('.section').filter({ hasText: section }).getByText('Hidden from customers'),
    ).toBeVisible();
  });

  test('prices always show two decimals', async ({ page }) => {
    // "$7.5" on a menu is a typo, not a price.
    const prices = await page.locator('.item-price').allTextContents();

    expect(prices.length).toBeGreaterThan(0);
    for (const price of prices) {
      expect(price.trim()).toMatch(/^\$\d+\.\d{2}$/);
    }
  });
});

/** Removes a dish through the UI, confirmation and all, so tests do not silt up the database. */
async function removeItem(page: import('@playwright/test').Page, name: string): Promise<void> {
  await page.goto('/menu');
  const row = page.locator('.item').filter({ hasText: name });
  await row.getByRole('button', { name: `Options for ${name}` }).click();
  await page.getByRole('menuitem', { name: 'Remove' }).click();
  await page.getByRole('button', { name: 'Remove', exact: true }).click();
  await expect(page.locator('.item').filter({ hasText: name })).toHaveCount(0);
}
