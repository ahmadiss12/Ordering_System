import { expect, test } from '@playwright/test';
import { ACCOUNTS, signIn } from './helpers';

test.describe('signing in', () => {
  test('takes a restaurant owner to their dashboard', async ({ page }) => {
    await signIn(page);

    await expect(page.getByRole('heading', { name: 'Overview' })).toBeVisible();
    // Scoped to one restaurant by the restaurant_id claim, not showing the whole platform.
    await expect(page.getByRole('heading', { name: 'FriesLab' })).toBeVisible();
    await expect(page.locator('mat-card')).toHaveCount(1);
  });

  test('refuses a wrong password without saying which half was wrong', async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Email').fill(ACCOUNTS.owner.email);
    await page.getByLabel('Password').fill('not-the-password');
    await page.getByRole('button', { name: 'Sign in' }).click();

    const alert = page.getByRole('alert');
    await expect(alert).toBeVisible();
    await expect(alert).not.toContainText(/no such account|unknown email|incorrect password/i);
    await expect(page).toHaveURL(/\/login/);
  });

  test('survives a reload', async ({ page }) => {
    await signIn(page);

    // The access token lives in memory only, so this is the refresh path doing its job rather
    // than a token that was simply still lying around.
    await page.reload();

    await expect(page.getByRole('heading', { name: 'Overview' })).toBeVisible();
    await expect(page).not.toHaveURL(/\/login/);
  });

  test('sends a signed-out visitor to the login page', async ({ page }) => {
    await page.goto('/menu');

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });

  test('keeps owner-only pages away from staff', async ({ page }) => {
    await signIn(page, ACCOUNTS.staff);

    await expect(page.getByRole('link', { name: 'Settings' })).toHaveCount(0);

    // The sidenav hiding it is cosmetic; typing the URL is the part that has to hold.
    await page.goto('/settings');
    await expect(page.getByText('You do not have access to this page')).toBeVisible();
  });
});
