import { TestBed } from '@angular/core/testing';
import { PagedResultOfRestaurantSummary, RestaurantSummary, RestaurantsClient } from 'api-client';
import { Observable, of, throwError } from 'rxjs';
import { CatalogStore } from './catalog-store';

/**
 * The list a customer opens on.
 *
 * <p>
 * What matters is the order: somebody hungry now wants the kitchens that will cook now, and a
 * list sorted by name buries them under whoever is called Aabo. Everything else here is about
 * not lying when the list could not be fetched.
 * </p>
 */
describe('CatalogStore', () => {
  let client: FakeRestaurantsClient;

  beforeEach(() => {
    client = new FakeRestaurantsClient();
    TestBed.configureTestingModule({
      providers: [CatalogStore, { provide: RestaurantsClient, useValue: client }],
    });
  });

  it('puts the kitchens that can cook now first', async () => {
    client.returns([
      restaurant('Aabo', { isOpenNow: false }),
      restaurant('Zaatar', { isOpenNow: true }),
    ]);

    const store = await loaded();

    // Alphabetical would bury the only place taking orders under the one that is shut.
    expect(store.restaurants().map((r) => r.name)).toEqual(['Zaatar', 'Aabo']);
  });

  it('ranks a busy kitchen above a closed one', async () => {
    client.returns([
      restaurant('Closed One', { isOpenNow: false }),
      restaurant('Busy One', { isOpenNow: true, isAcceptingOrders: false }),
      restaurant('Open One', { isOpenNow: true }),
    ]);

    const store = await loaded();

    // Too busy is twenty minutes away; closed is tomorrow. They are not the same answer and
    // should not sit together at the bottom.
    expect(store.restaurants().map((r) => r.name)).toEqual(['Open One', 'Busy One', 'Closed One']);
  });

  it('falls back to name between restaurants in the same state', async () => {
    client.returns([restaurant('Beta'), restaurant('Alpha')]);

    const store = await loaded();

    expect(store.restaurants().map((r) => r.name)).toEqual(['Alpha', 'Beta']);
  });

  it('counts only the ones actually taking orders', async () => {
    client.returns([
      restaurant('Open', { isOpenNow: true }),
      restaurant('Busy', { isOpenNow: true, isAcceptingOrders: false }),
      restaurant('Shut', { isOpenNow: false }),
    ]);

    const store = await loaded();

    // The headline says how many kitchens are taking orders. A paused one is not.
    expect(store.openCount()).toBe(1);
  });

  it('does not call an unfetched list empty', async () => {
    client.fails();
    const store = TestBed.inject(CatalogStore);
    await store.load();

    // "No restaurants yet" is a sentence about the platform. "Could not load" is a sentence about
    // the connection, and they must not be swapped.
    expect(store.isEmpty()).toBe(false);
    expect(store.loaded()).toBe(false);
    expect(store.error()).not.toBeNull();
  });

  it('does call a fetched empty list empty', async () => {
    client.returns([]);
    const store = await loaded();

    expect(store.isEmpty()).toBe(true);
    expect(store.error()).toBeNull();
  });

  async function loaded(): Promise<CatalogStore> {
    const store = TestBed.inject(CatalogStore);
    await store.load();
    return store;
  }
});

function restaurant(name: string, overrides: Partial<RestaurantSummary> = {}): RestaurantSummary {
  return {
    id: name,
    name,
    slug: name.toLowerCase(),
    description: null,
    logoUrl: null,
    minOrderUsd: 0,
    defaultPrepMinutes: 20,
    isAcceptingOrders: true,
    isOpenNow: true,
    deliveryFeeUsd: null,
    estimatedMinutes: null,
    nextOpening: null,
    ...overrides,
  } as RestaurantSummary;
}

class FakeRestaurantsClient {
  private items: RestaurantSummary[] = [];
  private failing = false;

  returns(items: RestaurantSummary[]): void {
    this.items = items;
  }

  fails(): void {
    this.failing = true;
  }

  list(): Observable<PagedResultOfRestaurantSummary> {
    if (this.failing) {
      return throwError(() => new Error('the API is not answering'));
    }

    return of({
      items: this.items,
      page: 1,
      pageSize: 20,
      totalCount: this.items.length,
    } as PagedResultOfRestaurantSummary);
  }
}
