import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ApiException,
  CategoryResponse,
  MenuItemResponse,
  OptionResponse,
  OptionGroupResponse,
  RestaurantCategoriesClient,
  RestaurantMenuItemsClient,
  RestaurantOptionGroupsClient,
} from 'api-client';
import { Observable, of, throwError } from 'rxjs';
import { MenuStore } from './menu-store';

/**
 * The store's job is to turn two flat lists into the menu as someone reads it, and to keep the
 * screen honest about what the server actually accepted.
 */
describe('MenuStore', () => {
  let categories: CategoryResponse[];
  let items: MenuItemResponse[];
  let update: ReturnType<typeof vi.fn>;
  let create: ReturnType<typeof vi.fn>;
  let groups: OptionGroupResponse[];
  let groupApi: {
    list: () => Observable<OptionGroupResponse[]>;
    create: ReturnType<typeof vi.fn>;
    update: ReturnType<typeof vi.fn>;
    addOption: ReturnType<typeof vi.fn>;
    updateOption: ReturnType<typeof vi.fn>;
  };
  let itemApi: {
    list: () => Observable<MenuItemResponse[]>;
    create: ReturnType<typeof vi.fn>;
    update: ReturnType<typeof vi.fn>;
    delete: ReturnType<typeof vi.fn>;
    setAvailability: ReturnType<typeof vi.fn>;
    uploadImage: ReturnType<typeof vi.fn>;
    removeImage: ReturnType<typeof vi.fn>;
    attachOptionGroup: ReturnType<typeof vi.fn>;
    detachOptionGroup: ReturnType<typeof vi.fn>;
    listOptionGroups: ReturnType<typeof vi.fn>;
  };

  function build(): MenuStore {
    update = vi.fn((id: string, request: Record<string, unknown>) =>
      of({ ...categories.find((c) => c.id === id), ...request } as CategoryResponse),
    );
    create = vi.fn((request: Record<string, unknown>) =>
      of({ id: 'new', isActive: true, ...request } as CategoryResponse),
    );

    groupApi = {
      list: () => of(groups),
      create: vi.fn((request: Record<string, unknown>) =>
        of({ id: 'new-group', options: [], ...request } as unknown as OptionGroupResponse),
      ),
      update: vi.fn((id: string, request: Record<string, unknown>) =>
        of({ ...groups.find((g) => g.id === id), ...request } as unknown as OptionGroupResponse),
      ),
      addOption: vi.fn((groupId: string, request: Record<string, unknown>) =>
        of({ id: 'new-option', ...request } as unknown as OptionResponse),
      ),
      updateOption: vi.fn((optionId: string, request: Record<string, unknown>) =>
        of({ id: optionId, ...request } as unknown as OptionResponse),
      ),
    };

    itemApi = {
      list: () => of(items),
      create: vi.fn((request: Record<string, unknown>) =>
        of({ id: 'new-item', isAvailable: true, ...request } as unknown as MenuItemResponse),
      ),
      update: vi.fn((id: string, request: Record<string, unknown>) =>
        of({ ...items.find((i) => i.id === id), ...request } as unknown as MenuItemResponse),
      ),
      delete: vi.fn(() => of(undefined)),
      setAvailability: vi.fn((id: string, request: { isAvailable: boolean }) =>
        of({ ...items.find((i) => i.id === id), ...request } as unknown as MenuItemResponse),
      ),
      uploadImage: vi.fn((id: string) =>
        of({
          ...items.find((i) => i.id === id),
          id,
          imageUrl: '/media/x.webp',
        } as unknown as MenuItemResponse),
      ),
      attachOptionGroup: vi.fn(() => of(undefined)),
      detachOptionGroup: vi.fn(() => of(undefined)),
      listOptionGroups: vi.fn(() => of([])),
      removeImage: vi.fn((id: string) =>
        of({
          ...items.find((i) => i.id === id),
          imageUrl: undefined,
        } as unknown as MenuItemResponse),
      ),
    };

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        MenuStore,
        {
          provide: RestaurantCategoriesClient,
          useValue: { list: () => of(categories), create, update },
        },
        { provide: RestaurantMenuItemsClient, useValue: itemApi },
        { provide: RestaurantOptionGroupsClient, useValue: groupApi },
      ],
    });

    return TestBed.inject(MenuStore);
  }

  beforeEach(() => {
    categories = [
      category('drinks', 'Drinks', 2),
      category('burgers', 'Burgers', 0),
      category('fries', 'Fries', 1),
    ];
    groups = [
      {
        id: 'sauces',
        name: 'Sauces',
        minSelect: 0,
        maxSelect: 3,
        sortOrder: 0,
        options: [{ id: 'garlic', name: 'Garlic' }],
      } as unknown as OptionGroupResponse,
    ];
    items = [
      item('a', 'burgers', 'Double', 1),
      item('b', 'burgers', 'Classic', 0),
      item('c', 'fries', 'Cheese', 0),
    ];
  });

  afterEach(() => TestBed.resetTestingModule());

  it('groups items under their section, both in sort order', async () => {
    const store = build();
    await store.load();

    const sections = store.sections();

    expect(sections.map((s) => s.category.name)).toEqual(['Burgers', 'Fries', 'Drinks']);
    expect(sections[0].items.map((i) => i.name)).toEqual(['Classic', 'Double']);
    expect(sections[2].items).toEqual([]);
  });

  it('surfaces items whose section is missing instead of dropping them', async () => {
    // Silently not drawing it is the bad outcome: a dish disappears and nobody knows why.
    items.push(item('lost', 'deleted-category', 'Orphan', 0));
    const store = build();
    await store.load();

    expect(store.orphanedItems().map((i) => i.name)).toEqual(['Orphan']);
  });

  it('adds a new section after the existing ones', async () => {
    const store = build();
    await store.load();

    await store.createCategory('Desserts');

    // Inserting anywhere else would reorder a menu nobody asked to reorder.
    expect(create).toHaveBeenCalledWith({ name: 'Desserts', sortOrder: 3 });
    expect(store.sections().at(-1)?.category.name).toBe('Desserts');
  });

  it('swaps a section with its neighbour when moved', async () => {
    const store = build();
    await store.load();

    await store.moveCategory(
      categories.find((c) => c.id === 'fries')!,
      -1,
    );

    expect(update).toHaveBeenCalledTimes(2);
    expect(store.sections().map((s) => s.category.name)).toEqual(['Fries', 'Burgers', 'Drinks']);
  });

  it('does nothing at the top of the list', async () => {
    const store = build();
    await store.load();

    await store.moveCategory(
      categories.find((c) => c.id === 'burgers')!,
      -1,
    );

    expect(update).not.toHaveBeenCalled();
  });

  it('keeps the whole field when only one part of a section changes', async () => {
    const store = build();
    await store.load();

    await store.renameCategory(
      categories.find((c) => c.id === 'drinks')!,
      'Cold Drinks',
    );

    // The endpoint replaces the row, so a rename that omitted sortOrder would silently move the
    // section to position zero.
    expect(update).toHaveBeenCalledWith('drinks', {
      name: 'Cold Drinks',
      sortOrder: 2,
      isActive: true,
    });
  });

  it('reports what the server said when a write is refused', async () => {
    const store = build();
    await store.load();

    update.mockReturnValue(problem(400, { errors: { Name: ['Name is required.'] } }));

    const ok = await store.renameCategory(categories[0], '');

    expect(ok).toBe(false);
    expect(store.error()).toBe('Name is required.');
    expect(store.saving()).toBe(false);
  });

  it('falls back to its own wording when the failure carries none', async () => {
    const store = build();
    await store.load();

    update.mockReturnValue(throwError(() => new Error('offline')));

    await store.renameCategory(categories[0], 'Anything');

    expect(store.error()).toBe('Could not save the section.');
  });

  // ------------------------------------------------------------------ items

  it('uploads the photo only after the item exists', async () => {
    const store = build();
    await store.load();

    const photo = new File(['x'], 'burger.png', { type: 'image/png' });
    await store.createItem(newItemRequest(), photo);

    // The upload endpoint needs an id, which does not exist until create returns.
    expect(itemApi.create).toHaveBeenCalledTimes(1);
    expect(itemApi.uploadImage).toHaveBeenCalledWith('new-item', {
      data: photo,
      fileName: 'burger.png',
    });
  });

  it('does not touch the image endpoint when no photo was chosen', async () => {
    const store = build();
    await store.load();

    await store.createItem(newItemRequest());

    expect(itemApi.uploadImage).not.toHaveBeenCalled();
  });

  it('keeps the item when only its photo fails to upload', async () => {
    const store = build();
    await store.load();

    itemApi.uploadImage.mockReturnValue(throwError(() => new Error('upload failed')));

    const ok = await store.createItem(newItemRequest(), new File(['x'], 'p.png'));

    // Rolling back would delete a dish somebody just typed out because a photo did not stick.
    expect(ok).toBe(false);
    expect(store.items().some((i) => i.id === 'new-item')).toBe(true);
    expect(store.error()).toBe('Could not add the item.');
  });

  it('changes availability through its own endpoint, not the item form', async () => {
    const store = build();
    await store.load();

    await store.setItemAvailable(items[0], false);

    // The update endpoint carries no availability field: the API separates them because this
    // one is pressed mid-service.
    expect(itemApi.setAvailability).toHaveBeenCalledWith('a', { isAvailable: false });
    expect(itemApi.update).not.toHaveBeenCalled();
    expect(store.items().find((i) => i.id === 'a')?.isAvailable).toBe(false);
  });

  it('takes a removed item off the list', async () => {
    const store = build();
    await store.load();

    await store.deleteItem(items[0]);

    expect(itemApi.delete).toHaveBeenCalledWith('a');
    expect(store.items().some((i) => i.id === 'a')).toBe(false);
  });

  it('moves an item only among its own section', async () => {
    const store = build();
    await store.load();

    // 'c' is alone in Fries, so there is no neighbour to swap with even though other items exist.
    await store.moveItem(items[2], -1);

    expect(itemApi.update).not.toHaveBeenCalled();
  });

  it('swaps an item with the one above it', async () => {
    const store = build();
    await store.load();

    // 'a' (Double, sortOrder 1) sits below 'b' (Classic, sortOrder 0) in Burgers.
    await store.moveItem(items[0], -1);

    expect(itemApi.update).toHaveBeenCalledTimes(2);
    expect(store.sections()[0].items.map((i) => i.name)).toEqual(['Double', 'Classic']);
  });

  it('puts a new item after the ones already in that section', async () => {
    const store = build();
    await store.load();

    expect(store.nextItemSortOrder('burgers')).toBe(2);
    expect(store.nextItemSortOrder('drinks')).toBe(0);
  });

  // ------------------------------------------------------------------ option groups

  it('sends no maximum rather than a maximum of null', async () => {
    const store = build();
    await store.load();

    await store.createOptionGroup('Extras', { minSelect: 0, maxSelect: null });

    // The generated client omits undefined from the body; a literal null would be read by the
    // API as a value rather than as "no limit".
    expect(groupApi.create).toHaveBeenCalledWith({
      name: 'Extras',
      minSelect: 0,
      maxSelect: undefined,
      sortOrder: 1,
    });
  });

  it('keeps a group its choices when only the rule changes', async () => {
    const store = build();
    await store.load();

    // The update response carries no options; blanking them would empty the group on screen
    // every time somebody corrected its name.
    groupApi.update.mockReturnValue(
      of({ id: 'sauces', name: 'Sauces', minSelect: 1, maxSelect: 1, sortOrder: 0 }),
    );

    await store.updateOptionGroup(groups[0], { rule: { minSelect: 1, maxSelect: 1 } });

    expect(store.optionGroups()[0].options).toHaveLength(1);
    expect(store.optionGroups()[0].minSelect).toBe(1);
  });

  it('adds a choice to the group it belongs to', async () => {
    const store = build();
    await store.load();

    await store.addOption(groups[0], {
      name: 'Spicy Mayo',
      priceDeltaUsd: 0.5,
      maxQuantity: 1,
      sortOrder: 1,
    });

    expect(groupApi.addOption).toHaveBeenCalledWith(
      'sauces',
      expect.objectContaining({
        name: 'Spicy Mayo',
      }),
    );
    expect(store.optionGroups()[0].options.map((o) => o.name)).toEqual(['Garlic', 'Spicy Mayo']);
  });

  it('attaches a group to an item without an override', async () => {
    const store = build();
    await store.load();

    await store.attachOptionGroup('item-1', {
      optionGroupId: 'sauces',
      sortOrder: 0,
      minSelectOverride: undefined,
      maxSelectOverride: undefined,
    });

    // Undefined, not zero: zero is a real minimum, and would silently become an override.
    expect(itemApi.attachOptionGroup).toHaveBeenCalledWith('item-1', {
      optionGroupId: 'sauces',
      sortOrder: 0,
      minSelectOverride: undefined,
      maxSelectOverride: undefined,
    });
  });

  it("reports rather than throws when an item's groups cannot be read", async () => {
    const store = build();
    await store.load();

    itemApi.listOptionGroups.mockReturnValue(throwError(() => new Error('offline')));

    const result = await store.loadItemOptionGroups('item-1');

    expect(result).toBeNull();
    expect(store.error()).toBe("Could not load this item's options.");
  });

  // ------------------------------------------------------------------ helpers

  function newItemRequest() {
    return {
      categoryId: 'burgers',
      name: 'New',
      description: undefined,
      basePriceUsd: 9,
      sortOrder: 2,
    };
  }

  function category(id: string, name: string, sortOrder: number): CategoryResponse {
    return { id, name, sortOrder, isActive: true } as CategoryResponse;
  }

  function item(id: string, categoryId: string, name: string, sortOrder: number): MenuItemResponse {
    return {
      id,
      categoryId,
      name,
      description: undefined,
      basePriceUsd: 5,
      imageUrl: undefined,
      isAvailable: true,
      sortOrder,
    } as unknown as MenuItemResponse;
  }

  function problem(status: number, body: Record<string, unknown>): Observable<never> {
    return throwError(() => new ApiException('failed', status, JSON.stringify(body), {}, null));
  }
});
