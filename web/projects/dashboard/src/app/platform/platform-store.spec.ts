import { TestBed } from '@angular/core/testing';
import { PlatformRestaurantResponse, PlatformRestaurantsClient } from 'api-client';
import { Observable, of, throwError } from 'rxjs';
import { PlatformStore } from './platform-store';

/**
 * The platform list.
 *
 * <p>
 * Both fields here are somebody else's livelihood, so what is worth pinning is that the screen
 * never shows a stale number next to a switch, and that a failure says which restaurant it was
 * about rather than "something went wrong".
 * </p>
 */
describe('PlatformStore', () => {
  let client: FakePlatformClient;

  beforeEach(() => {
    client = new FakePlatformClient();
    TestBed.configureTestingModule({
      providers: [PlatformStore, { provide: PlatformRestaurantsClient, useValue: client }],
    });
  });

  it('counts the hidden restaurants', async () => {
    client.returns([restaurant('a', 'Open One'), restaurant('b', 'Shut One', { isActive: false })]);
    const store = await loaded();

    expect(store.hiddenCount()).toBe(1);
  });

  it('reloads the whole list after a change, not just the row that changed', async () => {
    client.returns([restaurant('a', 'One', { liveOrderCount: 3 })]);
    const store = await loaded();

    // The live-order count moves on its own while somebody is looking at this screen. Splicing
    // the response for one row back in would leave a number that was true when the page opened
    // sitting next to a switch that hides a restaurant, which is the wrong one to be reading.
    //
    // The write echoes the row it wrote and the list has moved on since, which is what makes the
    // two distinguishable. Without that they agree, and the test passes either way — as it did
    // when the fake answered both from one array.
    client.respondsWith(restaurant('a', 'One', { isActive: false, liveOrderCount: 3 }));
    client.returns([restaurant('a', 'One', { isActive: false, liveOrderCount: 5 })]);
    await store.setListing(store.rows()[0], false);

    expect(store.rows()[0].liveOrderCount).toBe(5);
    expect(store.hiddenCount()).toBe(1);
  });

  it('names the restaurant a failed rate change was about', async () => {
    client.returns([restaurant('a', 'Mezze House'), restaurant('b', 'FriesLab')]);
    const store = await loaded();

    client.fails();
    await store.setCommission(store.rows()[0], 12);

    expect(store.error()).toContain('Mezze House');
  });

  it('says which way a failed listing change was going', async () => {
    client.returns([restaurant('a', 'FriesLab', { isActive: false })]);
    const store = await loaded();

    client.fails();
    await store.setListing(store.rows()[0], true);

    // "Could not hide FriesLab" when somebody pressed "List again" would send them looking for
    // the wrong problem.
    expect(store.error()).toContain('list FriesLab');
  });

  it('leaves the list alone when a change fails', async () => {
    client.returns([restaurant('a', 'One', { commissionPercent: 15 })]);
    const store = await loaded();

    client.fails();
    await store.setCommission(store.rows()[0], 40);

    // The reload never happened, so the screen still shows what the server last confirmed rather
    // than the number somebody typed into a box that did not save.
    expect(store.rows()[0].commissionPercent).toBe(15);
  });

  it('does not pretend to have a list it could not load', async () => {
    client.fails();
    const store = TestBed.inject(PlatformStore);
    await store.load();

    // An empty list drawn confidently would say the platform has no restaurants on it.
    expect(store.loaded()).toBe(false);
    expect(store.error()).not.toBeNull();
  });

  async function loaded(): Promise<PlatformStore> {
    const store = TestBed.inject(PlatformStore);
    await store.load();
    return store;
  }
});

function restaurant(
  id: string,
  name: string,
  overrides: Partial<PlatformRestaurantResponse> = {},
): PlatformRestaurantResponse {
  return {
    id,
    name,
    slug: name.toLowerCase().replace(/\s+/g, '-'),
    phone: '+96170000000',
    isActive: true,
    isAcceptingOrders: true,
    commissionPercent: 15,
    liveOrderCount: 0,
    createdAt: new Date(),
    ...overrides,
  } as PlatformRestaurantResponse;
}

class FakePlatformClient {
  private restaurants: PlatformRestaurantResponse[] = [];
  private writeResponse: PlatformRestaurantResponse | null = null;
  private failing = false;

  /** What a subsequent list() answers. */
  returns(restaurants: PlatformRestaurantResponse[]): void {
    this.restaurants = restaurants;
  }

  /**
   * What a write echoes back, when a test needs it to differ from the list.
   *
   * A real endpoint returns the row it just wrote, which is one row and already a moment old.
   * Deriving it from the same array the list answers from made the two agree by construction,
   * and a store that ignored the reload entirely passed.
   */
  respondsWith(row: PlatformRestaurantResponse): void {
    this.writeResponse = row;
  }

  fails(): void {
    this.failing = true;
  }

  list(): Observable<PlatformRestaurantResponse[]> {
    return this.failing ? this.broken() : of(this.restaurants);
  }

  setCommission(
    restaurantId: string,
    body: { commissionPercent: number },
  ): Observable<PlatformRestaurantResponse> {
    if (this.failing) {
      return this.broken();
    }

    const found = this.restaurants.find((r) => r.id === restaurantId)!;
    return of(this.writeResponse ?? { ...found, commissionPercent: body.commissionPercent });
  }

  setListing(
    restaurantId: string,
    body: { isActive: boolean },
  ): Observable<PlatformRestaurantResponse> {
    if (this.failing) {
      return this.broken();
    }

    const found = this.restaurants.find((r) => r.id === restaurantId)!;
    return of(this.writeResponse ?? { ...found, isActive: body.isActive });
  }

  private broken<T>(): Observable<T> {
    return throwError(() => new Error('the API is not answering'));
  }
}
