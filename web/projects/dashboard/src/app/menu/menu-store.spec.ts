import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ApiException,
  CategoryResponse,
  MenuItemResponse,
  RestaurantCategoriesClient,
  RestaurantMenuItemsClient,
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

  function build(): MenuStore {
    update = vi.fn((id: string, request: Record<string, unknown>) =>
      of({ ...categories.find((c) => c.id === id), ...request } as CategoryResponse),
    );
    create = vi.fn((request: Record<string, unknown>) =>
      of({ id: 'new', isActive: true, ...request } as CategoryResponse),
    );

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        MenuStore,
        {
          provide: RestaurantCategoriesClient,
          useValue: { list: () => of(categories), create, update },
        },
        { provide: RestaurantMenuItemsClient, useValue: { list: () => of(items) } },
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

  // ------------------------------------------------------------------ helpers

  function category(id: string, name: string, sortOrder: number): CategoryResponse {
    return { id, name, sortOrder, isActive: true } as CategoryResponse;
  }

  function item(id: string, categoryId: string, name: string, sortOrder: number): MenuItemResponse {
    return {
      id,
      categoryId,
      name,
      description: null,
      basePriceUsd: 5,
      imageUrl: null,
      isAvailable: true,
      sortOrder,
    } as unknown as MenuItemResponse;
  }

  function problem(status: number, body: Record<string, unknown>): Observable<never> {
    return throwError(() => new ApiException('failed', status, JSON.stringify(body), {}, null));
  }
});
