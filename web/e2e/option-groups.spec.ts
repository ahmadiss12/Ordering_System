import { expect, test } from '@playwright/test';
import { signIn, uniqueName } from './helpers';

/**
 * The screen the phase plan called the interesting one: two integers presented as a sentence.
 */
test.describe('option groups', () => {
  test.beforeEach(async ({ page }) => {
    await signIn(page);
    await page.goto('/menu');
    await page.getByRole('tab', { name: 'Option groups' }).click();
  });

  test('describes each rule in words rather than numbers', async ({ page }) => {
    const size = page.locator('.section').filter({ hasText: 'Size' }).first();
    const sauces = page.locator('.section').filter({ hasText: 'Sauces' }).first();

    // The seeded groups store (1,1) and (0,3). Neither number appears on screen.
    await expect(size.getByText('Required · Choose 1')).toBeVisible();
    await expect(sauces.getByText('Optional · Choose up to 3')).toBeVisible();
  });

  test('previews the customer sentence while a rule is being chosen', async ({ page }) => {
    await page.getByRole('button', { name: 'Add option group' }).click();

    const dialog = page.getByRole('dialog');
    // The pair this control exists for: same maximum, opposite meaning.
    await dialog.getByRole('radio', { name: /Optional — one at most/ }).check();
    await expect(dialog.getByText('Choose 1 — optional')).toBeVisible();

    await dialog.getByRole('radio', { name: /Required — exactly one/ }).check();
    await expect(dialog.getByText('Customers will see')).toContainText('Choose 1');
    await expect(dialog.getByText('Choose 1 — optional')).toHaveCount(0);

    await dialog.getByRole('button', { name: 'Cancel' }).click();
  });

  test('shows the numbers only when the named rules do not fit', async ({ page }) => {
    await page.getByRole('button', { name: 'Add option group' }).click();
    const dialog = page.getByRole('dialog');

    await expect(dialog.getByRole('spinbutton', { name: 'At least' })).toHaveCount(0);

    await dialog.getByRole('radio', { name: /Something else/ }).check();

    // By role, not by label: "At most" is also the wording of one of the radios above.
    const atLeast = dialog.getByRole('spinbutton', { name: 'At least' });
    const atMost = dialog.getByRole('spinbutton', { name: 'At most' });

    await expect(atLeast).toBeVisible();
    await atLeast.fill('2');
    await atMost.fill('4');
    await expect(dialog.getByText('Customers will see')).toContainText('Choose 2 to 4');

    // And it refuses what the database would refuse, in a sentence rather than a SQL error.
    await atMost.fill('1');
    await expect(dialog.getByText('cannot be larger than')).toBeVisible();

    await dialog.getByRole('button', { name: 'Cancel' }).click();
  });

  test('says whether a dish follows its group or overrides it', async ({ page }) => {
    // Creates its own group rather than borrowing a seeded one. The seeder already attaches
    // groups to dishes, and gives the last one a MaxSelectOverride — so a test that assumed
    // "not attached, no override" passed or failed depending on what had run before it, and
    // detaching afterwards quietly deleted seeded data for the next run.
    const group = uniqueName('E2E Group');

    await page.getByRole('button', { name: 'Add option group' }).click();
    const groupDialog = page.getByRole('dialog');
    await groupDialog.getByLabel('Group name').fill(group);
    await groupDialog.getByRole('radio', { name: /Required — exactly one/ }).check();
    await groupDialog.getByRole('button', { name: 'Create group' }).click();
    await expect(page.locator('.section').filter({ hasText: group })).toBeVisible();

    await page.getByRole('tab', { name: 'Items' }).click();
    const row = page.locator('.item').filter({ hasText: 'Classic Smash' }).first();
    await row.getByRole('button', { name: /Options for/ }).click();
    await page.getByRole('menuitem', { name: 'Choices' }).click();

    const dialog = page.getByRole('dialog');
    const mine = dialog.locator('.group').filter({ hasText: group });

    await mine.getByRole('checkbox').check();

    // A resolved number alone could not tell this apart from a deliberate override, which is
    // why the API returns the unresolved values too.
    await expect(mine.getByText('Same as the group')).toBeVisible();
    await expect(mine.getByText('Required · Choose 1')).toBeVisible();

    await mine.getByRole('button', { name: /Set a different rule/ }).click();
    await dialog.getByRole('radio', { name: /Optional — one at most/ }).check();
    await dialog.getByRole('button', { name: 'Save for this dish' }).click();

    await expect(mine.getByText('Changed for this dish')).toBeVisible();
    await expect(mine.getByText('Optional · Choose 1 — optional')).toBeVisible();

    await mine.getByRole('button', { name: "Use the group's rule" }).click();
    await expect(mine.getByText('Same as the group')).toBeVisible();

    // Leave the dish as it was found. The group itself stays: there is no endpoint to delete
    // one, and an empty extra group is harmless next to a dish wearing a rule it never had.
    await mine.getByRole('checkbox').uncheck();
    await expect(mine.getByText('Same as the group')).toHaveCount(0);
    await dialog.getByRole('button', { name: 'Done' }).click();
  });
});
