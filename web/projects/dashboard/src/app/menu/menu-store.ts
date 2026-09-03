import { Injectable, computed, inject, signal } from '@angular/core';
import {
  AttachedOptionGroupResponse,
  AttachOptionGroupRequest,
  CategoryResponse,
  CreateMenuItemRequest,
  CreateOptionRequest,
  FileParameter,
  MenuItemResponse,
  OptionGroupResponse,
  RestaurantCategoriesClient,
  RestaurantMenuItemsClient,
  RestaurantOptionGroupsClient,
  describeError,
} from 'api-client';
import { SelectionRule } from './selection-rule';
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
  private readonly groupsClient = inject(RestaurantOptionGroupsClient);

  private readonly categoriesSignal = signal<readonly CategoryResponse[]>([]);
  private readonly itemsSignal = signal<readonly MenuItemResponse[]>([]);
  private readonly groupsSignal = signal<readonly OptionGroupResponse[]>([]);

  readonly categories = this.categoriesSignal.asReadonly();
  readonly items = this.itemsSignal.asReadonly();
  readonly optionGroups = this.groupsSignal.asReadonly();

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
      // All at once: independent reads, and the screen needs them before it can draw.
      const [categories, items, groups] = await Promise.all([
        firstValueFrom(this.categoriesClient.list()),
        firstValueFrom(this.itemsClient.list()),
        firstValueFrom(this.groupsClient.list()),
      ]);

      this.categoriesSignal.set(categories);
      this.itemsSignal.set(items);
      this.groupsSignal.set(groups);
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

  // ------------------------------------------------------------------ items

  /** Where a new item lands: after the ones already in that section. */
  nextItemSortOrder(categoryId: string): number {
    return highestSortOrder(this.itemsSignal().filter((i) => i.categoryId === categoryId)) + 1;
  }

  /**
   * Creates an item and, if one was chosen, uploads its photo.
   *
   * The photo is a second request because the endpoint needs an id that does not exist until the
   * first one returns. A failed upload leaves the item created and says so, rather than rolling
   * back a dish someone just typed out.
   */
  async createItem(request: CreateMenuItemRequest, photo?: File): Promise<boolean> {
    return this.write('Could not add the item.', async () => {
      const created = await firstValueFrom(this.itemsClient.create(request));
      this.itemsSignal.update((current) => [...current, created]);

      if (photo) {
        await this.uploadPhoto(created.id, photo);
      }
    });
  }

  async updateItem(
    item: MenuItemResponse,
    request: CreateMenuItemRequest,
    photo?: File,
  ): Promise<boolean> {
    return this.write('Could not save the item.', async () => {
      const updated = await firstValueFrom(this.itemsClient.update(item.id, request));
      this.replaceItem(updated);

      if (photo) {
        await this.uploadPhoto(item.id, photo);
      }
    });
  }

  /** The mid-service action: the kitchen ran out, and this is one press on the row. */
  async setItemAvailable(item: MenuItemResponse, isAvailable: boolean): Promise<boolean> {
    return this.write('Could not change availability.', async () => {
      const updated = await firstValueFrom(
        this.itemsClient.setAvailability(item.id, { isAvailable }),
      );
      this.replaceItem(updated);
    });
  }

  /**
   * Removes an item from the menu. A soft delete on the server — order lines point at this row
   * and have to keep resolving — so past orders still read correctly afterwards.
   */
  async deleteItem(item: MenuItemResponse): Promise<boolean> {
    return this.write('Could not remove the item.', async () => {
      await firstValueFrom(this.itemsClient.delete(item.id));
      this.itemsSignal.update((current) => current.filter((i) => i.id !== item.id));
    });
  }

  async removeItemPhoto(item: MenuItemResponse): Promise<boolean> {
    return this.write('Could not remove the photo.', async () => {
      const updated = await firstValueFrom(this.itemsClient.removeImage(item.id));
      this.replaceItem(updated);
    });
  }

  /** Moves an item one place within its own section, the same swap the sections use. */
  async moveItem(item: MenuItemResponse, direction: -1 | 1): Promise<boolean> {
    const siblings = this.itemsSignal()
      .filter((i) => i.categoryId === item.categoryId)
      .sort(bySortOrderThenName);

    const index = siblings.findIndex((i) => i.id === item.id);
    const neighbour = siblings[index + direction];

    if (!neighbour) {
      return true;
    }

    return this.write('Could not reorder the items.', async () => {
      const [mine, theirs] = await Promise.all([
        firstValueFrom(
          this.itemsClient.update(item.id, { ...toRequest(item), sortOrder: index + direction }),
        ),
        firstValueFrom(
          this.itemsClient.update(neighbour.id, { ...toRequest(neighbour), sortOrder: index }),
        ),
      ]);

      this.replaceItem(mine);
      this.replaceItem(theirs);
    });
  }

  private async uploadPhoto(itemId: string, photo: File): Promise<void> {
    const file: FileParameter = { data: photo, fileName: photo.name };
    const updated = await firstValueFrom(this.itemsClient.uploadImage(itemId, file));
    this.replaceItem(updated);
  }

  private replaceItem(updated: MenuItemResponse): void {
    this.itemsSignal.update((current) => current.map((i) => (i.id === updated.id ? updated : i)));
  }

  // ------------------------------------------------------------------ option groups

  async createOptionGroup(name: string, rule: SelectionRule): Promise<boolean> {
    const sortOrder = highestSortOrder(this.groupsSignal()) + 1;

    return this.write('Could not add the option group.', async () => {
      const created = await firstValueFrom(
        this.groupsClient.create({
          name,
          minSelect: rule.minSelect,
          maxSelect: rule.maxSelect ?? null,
          sortOrder,
        }),
      );
      this.groupsSignal.update((current) => [...current, created]);
    });
  }

  async updateOptionGroup(
    group: OptionGroupResponse,
    changes: { name?: string; rule?: SelectionRule },
  ): Promise<boolean> {
    return this.write('Could not save the option group.', async () => {
      const rule = changes.rule;

      const updated = await firstValueFrom(
        this.groupsClient.update(group.id, {
          name: changes.name ?? group.name,
          minSelect: rule ? rule.minSelect : group.minSelect,
          maxSelect: rule ? (rule.maxSelect ?? null) : group.maxSelect,
          sortOrder: group.sortOrder,
        }),
      );

      // The update response carries the group without its options, so the ones already loaded
      // are kept rather than blanking the list every time a name is corrected.
      this.replaceGroup({ ...updated, options: updated.options ?? group.options });
    });
  }

  async addOption(group: OptionGroupResponse, request: CreateOptionRequest): Promise<boolean> {
    return this.write('Could not add the option.', async () => {
      const created = await firstValueFrom(this.groupsClient.addOption(group.id, request));
      this.replaceGroup({ ...group, options: [...group.options, created] });
    });
  }

  async updateOption(
    group: OptionGroupResponse,
    optionId: string,
    request: CreateOptionRequest & { isAvailable: boolean },
  ): Promise<boolean> {
    return this.write('Could not save the option.', async () => {
      const updated = await firstValueFrom(this.groupsClient.updateOption(optionId, request));
      this.replaceGroup({
        ...group,
        options: group.options.map((o) => (o.id === optionId ? updated : o)),
      });
    });
  }

  // ------------------------------------------------------------------ groups on an item

  /**
   * The groups attached to one item, loaded on demand.
   *
   * Not held in the store: it belongs to whichever item is open, and caching it would mean
   * showing a stale set the next time somebody edited a different dish. Returns null on failure,
   * with the reason already in {@link error}.
   */
  async loadItemOptionGroups(
    itemId: string,
  ): Promise<readonly AttachedOptionGroupResponse[] | null> {
    try {
      return await firstValueFrom(this.itemsClient.listOptionGroups(itemId));
    } catch (error) {
      this.error.set(describeError(error, "Could not load this item's options."));
      return null;
    }
  }

  async attachOptionGroup(itemId: string, request: AttachOptionGroupRequest): Promise<boolean> {
    return this.write('Could not attach the option group.', async () => {
      await firstValueFrom(this.itemsClient.attachOptionGroup(itemId, request));
    });
  }

  async detachOptionGroup(itemId: string, optionGroupId: string): Promise<boolean> {
    return this.write('Could not remove the option group.', async () => {
      await firstValueFrom(this.itemsClient.detachOptionGroup(itemId, optionGroupId));
    });
  }

  private replaceGroup(updated: OptionGroupResponse): void {
    this.groupsSignal.update((current) => current.map((g) => (g.id === updated.id ? updated : g)));
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

/** The item's own values as an update request, for the fields a caller is not changing. */
function toRequest(item: MenuItemResponse): CreateMenuItemRequest {
  return {
    categoryId: item.categoryId,
    name: item.name,
    description: item.description,
    basePriceUsd: item.basePriceUsd,
    sortOrder: item.sortOrder,
  };
}

function highestSortOrder(rows: readonly { sortOrder: number }[]): number {
  return rows.reduce((highest, row) => Math.max(highest, row.sortOrder), -1);
}
