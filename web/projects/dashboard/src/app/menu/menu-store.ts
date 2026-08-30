import { Injectable, computed, inject, signal } from '@angular/core';
import {
  CategoryResponse,
  MenuItemResponse,
  RestaurantCategoriesClient,
  RestaurantMenuItemsClient,
  describeError,
} from 'api-client';
import { firstValueFrom } from 'rxjs';

/** A category together with the items filed under it, which is how the editor draws the menu. */
export interface MenuSection {
  readonly category: CategoryResponse;
  readonly items: readonly MenuItemResponse[];
}

/**
 * The menu the editor is working on, and every change made to it.
 *
 * Provided by the menu route rather than in root, so leaving the page drops the data instead of
 * holding a copy that goes stale while someone edits the menu on their phone.
 *
 * Writes go to the server first and update local state from the response. The alternative —
 * changing the screen immediately and reconciling later — reads as faster right up to the moment
 * a save fails, at which point the price on screen is not the price customers are being charged.
 * For a menu, that is the wrong trade.
 */
@Injectable()
export class MenuStore {
  private readonly categoriesClient = inject(RestaurantCategoriesClient);
  private readonly itemsClient = inject(RestaurantMenuItemsClient);

  private readonly categoriesSignal = signal<readonly CategoryResponse[]>([]);
  private readonly itemsSignal = signal<readonly MenuItemResponse[]>([]);

  readonly categories = this.categoriesSignal.asReadonly();
  readonly items = this.itemsSignal.asReadonly();

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  /** Set while a write is in flight, so the screen can disable the controls that would race it. */
  readonly saving = signal(false);

  /** The menu in display order: categories by sort order, each with its own items. */
  readonly sections = computed<readonly MenuSection[]>(() => {
    const items = this.itemsSignal();
    return [...this.categoriesSignal()].sort(bySortOrderThenName).map((category) => ({
      category,
      items: items.filter((item) => item.categoryId === category.id).sort(bySortOrderThenName),
    }));
  });

  /** Items whose category was deleted or is missing — otherwise they would vanish silently. */
  readonly orphanedItems = computed(() => {
    const known = new Set(this.categoriesSignal().map((c) => c.id));
    return this.itemsSignal().filter((item) => !known.has(item.categoryId));
  });

  readonly isEmpty = computed(
    () =>
      !this.loading() && this.categoriesSignal().length === 0 && this.itemsSignal().length === 0,
  );

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      // Both at once: they are independent reads and the screen needs both before it can draw.
      const [categories, items] = await Promise.all([
        firstValueFrom(this.categoriesClient.list()),
        firstValueFrom(this.itemsClient.list()),
      ]);

      this.categoriesSignal.set(categories);
      this.itemsSignal.set(items);
    } catch (error) {
      this.error.set(describeError(error, 'Could not load the menu.'));
    } finally {
      this.loading.set(false);
    }
  }

  // ------------------------------------------------------------------ categories

  async createCategory(name: string): Promise<boolean> {
    // New sections go to the end. Anywhere else would reorder a menu someone did not ask to
    // reorder, and moving it up afterwards is one press.
    const sortOrder = highestSortOrder(this.categoriesSignal()) + 1;

    return this.write('Could not add the section.', async () => {
      const created = await firstValueFrom(this.categoriesClient.create({ name, sortOrder }));
      this.categoriesSignal.update((current) => [...current, created]);
    });
  }

  async renameCategory(category: CategoryResponse, name: string): Promise<boolean> {
    return this.updateCategory(category, { name });
  }

  async setCategoryActive(category: CategoryResponse, isActive: boolean): Promise<boolean> {
    return this.updateCategory(category, { isActive });
  }

  /**
   * Moves a section one place up or down by swapping sort orders with its neighbour.
   *
   * Two writes, and no bulk endpoint to do it in one — but a swap only ever touches two rows,
   * where dragging an item across a long menu would rewrite every row it passed.
   */
  async moveCategory(category: CategoryResponse, direction: -1 | 1): Promise<boolean> {
    const ordered = [...this.categoriesSignal()].sort(bySortOrderThenName);
    const index = ordered.findIndex((c) => c.id === category.id);
    const neighbour = ordered[index + direction];

    if (!neighbour) {
      return true;
    }

    return this.write('Could not reorder the sections.', async () => {
      // Sort orders can collide or be equal in seeded data, so the positions are recomputed from
      // the displayed order rather than trusting the two stored numbers to differ.
      const mine = index;
      const theirs = index + direction;

      const [updatedMine, updatedTheirs] = await Promise.all([
        firstValueFrom(
          this.categoriesClient.update(category.id, {
            name: category.name,
            isActive: category.isActive,
            sortOrder: theirs,
          }),
        ),
        firstValueFrom(
          this.categoriesClient.update(neighbour.id, {
            name: neighbour.name,
            isActive: neighbour.isActive,
            sortOrder: mine,
          }),
        ),
      ]);

      this.replaceCategory(updatedMine);
      this.replaceCategory(updatedTheirs);
    });
  }

  private async updateCategory(
    category: CategoryResponse,
    changes: Partial<Pick<CategoryResponse, 'name' | 'isActive' | 'sortOrder'>>,
  ): Promise<boolean> {
    return this.write('Could not save the section.', async () => {
      // The endpoint replaces the row, so every field goes even when one changed.
      const updated = await firstValueFrom(
        this.categoriesClient.update(category.id, {
          name: changes.name ?? category.name,
          sortOrder: changes.sortOrder ?? category.sortOrder,
          isActive: changes.isActive ?? category.isActive,
        }),
      );
      this.replaceCategory(updated);
    });
  }

  private replaceCategory(updated: CategoryResponse): void {
    this.categoriesSignal.update((current) =>
      current.map((c) => (c.id === updated.id ? updated : c)),
    );
  }

  /**
   * Runs a write, reporting failure rather than throwing. Returns whether it succeeded, so a
   * dialog knows whether to close.
   */
  private async write(fallback: string, action: () => Promise<void>): Promise<boolean> {
    this.saving.set(true);
    this.error.set(null);

    try {
      await action();
      return true;
    } catch (error) {
      this.error.set(describeError(error, fallback));
      return false;
    } finally {
      this.saving.set(false);
    }
  }
}

function bySortOrderThenName(
  a: { sortOrder: number; name: string },
  b: { sortOrder: number; name: string },
): number {
  return a.sortOrder - b.sortOrder || a.name.localeCompare(b.name);
}

function highestSortOrder(rows: readonly { sortOrder: number }[]): number {
  return rows.reduce((highest, row) => Math.max(highest, row.sortOrder), -1);
}
