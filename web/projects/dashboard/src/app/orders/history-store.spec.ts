import { TestBed } from '@angular/core/testing';
import {
  FulfillmentType,
  OrderStatus,
  OrderSummaryResponse,
  PagedResultOfOrderSummaryResponse,
  RestaurantOrdersClient,
} from 'api-client';
import { Observable, of, throwError } from 'rxjs';
import { FINISHED_STATUSES, HISTORY_FILTERS, HistoryStore } from './history-store';

/**
 * The history's plumbing: what it asks for, and where it lands when somebody changes their mind.
 */
describe('HistoryStore', () => {
  let client: FakeOrdersClient;

  beforeEach(() => {
    client = new FakeOrdersClient();
    TestBed.configureTestingModule({
      providers: [HistoryStore, { provide: RestaurantOrdersClient, useValue: client }],
    });
  });

  it('asks for finished orders, newest first', async () => {
    await created();

    // Newest first is the opposite of the queue and cannot be fixed client-side once the list is
    // paged: the wrong end opens on the restaurant's first ever order.
    expect(client.lastStatuses).toEqual([...FINISHED_STATUSES]);
    expect(client.lastNewestFirst).toBe(true);
  });

  it('narrows to one status when a filter is chosen', async () => {
    const store = await created();

    await store.setFilter(HISTORY_FILTERS.find((f) => f.key === 'refused')!);

    expect(client.lastStatuses).toEqual([OrderStatus.Rejected]);
  });

  it('goes back to the first page when the filter changes', async () => {
    client.returns([row('a')], 100);
    const store = await created();

    await store.goTo(3);
    expect(store.page()).toBe(3);

    // Page 3 of a different question is not where anybody meant to land.
    await store.setFilter(HISTORY_FILTERS.find((f) => f.key === 'delivered')!);
    expect(store.page()).toBe(1);
  });

  it('will not page past either end', async () => {
    client.returns([row('a')], 30);
    const store = await created();

    await store.goTo(0);
    expect(store.page()).toBe(1);

    // 30 orders at 25 a page is two pages.
    await store.goTo(99);
    expect(store.page()).toBe(2);
  });

  it('knows when there is nowhere further to go', async () => {
    client.returns([row('a')], 10);
    const store = await created();

    expect(store.hasPrevious()).toBe(false);
    expect(store.hasNext()).toBe(false);
    expect(store.totalPages()).toBe(1);
  });

  it('clears the rows when a load fails', async () => {
    client.returns([row('a')], 1);
    const store = await created();
    expect(store.orders()).toHaveLength(1);

    client.fails();
    await store.load();

    // The opposite of the queue, on purpose. A history that kept showing the previous filter's
    // rows after a failed load would be answering a question nobody asked.
    expect(store.orders()).toHaveLength(0);
    expect(store.error()).not.toBeNull();
  });

  it('offers a filter for every status an order can finish at', () => {
    // A finished status with no filter is a set of orders nobody can find.
    const covered = HISTORY_FILTERS.flatMap((f) => f.statuses);

    for (const status of FINISHED_STATUSES) {
      expect(covered).toContain(status);
    }
  });

  async function created(): Promise<HistoryStore> {
    const store = TestBed.inject(HistoryStore);
    await store.load();
    return store;
  }

  function row(id: string): OrderSummaryResponse {
    return {
      id,
      orderNumber: `FRIESLAB-260903-00${id}`,
      status: OrderStatus.Delivered,
      fulfillment: FulfillmentType.Pickup,
      placedAt: new Date(),
      totalUsd: 23.5,
      itemCount: 2,
      promisedMinutesMin: 20,
      promisedMinutesMax: 30,
      restaurantName: 'FriesLab',
      restaurantSlug: 'frieslab',
      customerName: 'Rita Customer',
      rejectionReason: null,
      availableTransitions: [],
    };
  }
});

class FakeOrdersClient {
  lastStatuses: OrderStatus[] | undefined;
  lastNewestFirst: boolean | undefined;

  private items: OrderSummaryResponse[] = [];
  private totalCount = 0;
  private failing = false;

  returns(items: OrderSummaryResponse[], totalCount = items.length): void {
    this.items = items;
    this.totalCount = totalCount;
    this.failing = false;
  }

  fails(): void {
    this.failing = true;
  }

  queue(
    status?: OrderStatus[],
    newestFirst?: boolean,
  ): Observable<PagedResultOfOrderSummaryResponse> {
    this.lastStatuses = status;
    this.lastNewestFirst = newestFirst;

    if (this.failing) {
      return throwError(() => new Error('the API is not answering'));
    }

    return of({
      items: this.items,
      page: 1,
      pageSize: 25,
      totalCount: this.totalCount,
      totalPages: Math.ceil(this.totalCount / 25),
      hasNextPage: false,
    } as PagedResultOfOrderSummaryResponse);
  }
}
