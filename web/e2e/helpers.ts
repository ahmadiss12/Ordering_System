import { APIRequestContext, Page, expect } from '@playwright/test';

export const API = process.env['E2E_API_URL'] ?? 'http://localhost:5080';

/** Seeded accounts, from DatabaseSeeder. */
export const ACCOUNTS = {
  owner: { email: 'owner@frieslab.test', password: 'Passw0rd!' },
  staff: { email: 'staff@frieslab.test', password: 'Passw0rd!' },
} as const;

export const RESTAURANT_SLUG = 'frieslab';

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
