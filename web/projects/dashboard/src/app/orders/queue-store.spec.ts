import { TestBed } from '@angular/core/testing';
import {
  ChangeOrderStatusRequest,
  FulfillmentType,
  MyOrdersClient,
  OrderDetailResponse,
  OrderStatus,
  OrderSummaryResponse,
  PagedResultOfOrderSummaryResponse,
  RejectionReason,
  RestaurantOrdersClient,
  RestaurantSettingsClient,
  RestaurantSettingsResponse,
} from 'api-client';
import { LiveStatus, OrderStream } from 'realtime';
import { Observable, of, throwError } from 'rxjs';
import { WritableSignal, signal } from '@angular/core';
import { QueueStore } from './queue-store';

/**
 * The board's plumbing: what makes it refresh, and what it does when refreshing fails.
 *
 * The urgency rules themselves are tested in queue-model.spec.ts, which needs no Angular at all.
 * What is here is everything that would leave a kitchen looking at a screen that is quietly wrong.
 */
describe('QueueStore', () => {
  let client: FakeOrdersClient;
  let moves: FakeMoveClient;
  let settings: FakeSettingsClient;
  let stream: FakeStream;

  beforeEach(() => {
    vi.useFakeTimers();
    client = new FakeOrdersClient();
    moves = new FakeMoveClient();
    settings = new FakeSettingsClient();
    stream = new FakeStream();

    TestBed.configureTestingModule({
      providers: [
        QueueStore,
        { provide: RestaurantOrdersClient, useValue: client },
        { provide: MyOrdersClient, useValue: moves },
        { provide: RestaurantSettingsClient, useValue: settings },
        { provide: OrderStream, useValue: stream },
      ],
    });
  });

  afterEach(() => vi.useRealTimers());

  // ------------------------------------------------------------------ what makes it refresh

  it('loads the board as soon as it exists', async () => {
    client.returns([row({ id: 'a' })]);

    const store = await created();

    expect(store.shown()).toBe(1);
    expect(store.loading()).toBe(false);
  });

  it('asks only for the statuses a kitchen works', async () => {
    await created();

    // The finished ones belong to the history screen. Fetching them here would push live orders
    // off a fifty-row page on a busy night.
    expect(client.lastStatuses).toEqual([
      OrderStatus.Placed,
      OrderStatus.Accepted,
      OrderStatus.Preparing,
      OrderStatus.ReadyForPickup,
      OrderStatus.OutForDelivery,
    ]);
  });

  it('refetches whenever the stream says something changed', async () => {
    const store = await created();
    expect(client.calls).toBe(1);

    client.returns([row({ id: 'a' }), row({ id: 'b' })]);
    stream.revisionSignal.set(1);
    await settle();

    // One trigger, whether that revision came from a pushed message, a reconnection or the poll
    // behind them. The store never learns which, and cannot behave differently for one of them.
    expect(client.calls).toBe(2);
    expect(store.shown()).toBe(2);
  });

  // ------------------------------------------------------------------ when it goes wrong

  it('keeps the orders it has when a refresh fails', async () => {
    client.returns([row({ id: 'a' })]);
    const store = await created();

    client.fails();
    stream.revisionSignal.set(1);
    await settle();

    // A kitchen mid-service is far better off with a board that is a few seconds stale and says
    // so than with an empty one.
    expect(store.shown()).toBe(1);
    expect(store.error()).not.toBeNull();
  });

  it('clears the error once a refresh works again', async () => {
    client.fails();
    const store = await created();
    expect(store.error()).not.toBeNull();

    client.returns([row({ id: 'a' })]);
    stream.revisionSignal.set(1);
    await settle();

    expect(store.error()).toBeNull();
  });

  // ------------------------------------------------------------------ what it shows

  it('files each order under the column its status belongs to', async () => {
    client.returns([
      row({ id: 'a', status: OrderStatus.Placed }),
      row({ id: 'b', status: OrderStatus.Preparing }),
      row({ id: 'c', status: OrderStatus.ReadyForPickup }),
      row({ id: 'd', status: OrderStatus.OutForDelivery }),
    ]);

    const store = await created();
    const byKey = new Map(store.columns().map((c) => [c.key, c.orders.length]));

    expect(byKey.get('new')).toBe(1);
    expect(byKey.get('accepted')).toBe(0);
    expect(byKey.get('cooking')).toBe(1);
    // Ready and out for delivery are the same moment in a kitchen's day: the food has left.
    expect(byKey.get('out')).toBe(2);
  });

  it('says when it is not showing everything', async () => {
    client.returns([row({ id: 'a' })], 60);

    const store = await created();

    // Silently showing fifty of sixty would have staff believing the board was the whole queue.
    expect(store.truncated()).toBe(true);
    expect(store.total()).toBe(60);
  });

  it('counts the rows that want attention', async () => {
    client.returns([
      row({ id: 'a', minutesAgo: 0 }),
      row({ id: 'b', minutesAgo: 3 }),
      row({ id: 'c', minutesAgo: 9 }),
    ]);

    const store = await created();

    expect(store.needingAttention()).toBe(2);
  });

  // ------------------------------------------------------------------ pressing a button

  it('sends the move and reloads the board', async () => {
    client.returns([row({ id: 'a' })]);
    const store = await created();

    const ok = await store.move('a', OrderStatus.Accepted);
    await settle();

    expect(ok).toBe(true);
    expect(moves.sent).toEqual([
      { orderId: 'a', to: OrderStatus.Accepted, reason: null, note: null },
    ]);

    // The server pushes the change too, which would eventually refresh this board — but waiting
    // for a socket round trip to redraw a button somebody just pressed feels broken.
    expect(client.calls).toBe(2);
  });

  it('carries the reason and note when one was given', async () => {
    client.returns([row({ id: 'a' })]);
    const store = await created();

    await store.move('a', OrderStatus.Rejected, RejectionReason.TooBusy, 'no fryer');
    await settle();

    expect(moves.sent[0]).toEqual({
      orderId: 'a',
      to: OrderStatus.Rejected,
      reason: RejectionReason.TooBusy,
      note: 'no fryer',
    });
  });

  it('reports a refused move and still reloads', async () => {
    client.returns([row({ id: 'a' })]);
    const store = await created();

    // The commonest failure by far: another tablet accepted it first. A refusal means this board
    // was showing something stale, which is exactly when it most needs re-reading.
    moves.fails();
    const ok = await store.move('a', OrderStatus.Accepted);
    await settle();

    expect(ok).toBe(false);
    expect(store.error()).not.toBeNull();
    expect(client.calls).toBe(2);
  });

  it('marks only the order being moved as busy', async () => {
    client.returns([row({ id: 'a' }), row({ id: 'b' })]);
    const store = await created();

    expect(store.moving()).toBeNull();

    const inFlight = store.move('a', OrderStatus.Accepted);
    expect(store.moving()).toBe('a');

    await inFlight;
    await settle();
    expect(store.moving()).toBeNull();
  });

  // ------------------------------------------------------------------ pausing orders

  it('reads whether the restaurant is taking orders', async () => {
    const store = await created();

    expect(store.accepting()).toBe(true);
  });

  it('pauses and resumes without leaving the board', async () => {
    const store = await created();

    // Owner-only Settings is the wrong place for this: the person reaching for it is a cook in
    // the middle of service, looking at this screen.
    await store.setAcceptingOrders(false);
    expect(settings.lastSet).toBe(false);
    expect(store.accepting()).toBe(false);

    await store.setAcceptingOrders(true);
    expect(store.accepting()).toBe(true);
  });

  it('hides the switch rather than shouting when it cannot be read', async () => {
    settings.fails();
    const store = await created();

    // A board full of perfectly readable orders must not carry an error because one extra call
    // failed. Null means the control is simply not drawn.
    expect(store.accepting()).toBeNull();
    expect(store.error()).toBeNull();
  });

  // ------------------------------------------------------------------ the clock

  it('turns an order late without anybody touching the server', async () => {
    client.returns([row({ id: 'a', minutesAgo: 0, status: OrderStatus.Placed })]);
    const store = await created();

    expect(store.orders()[0].urgency).toBe('calm');

    // Nothing changed on the server and nothing was refetched — the order simply sat there.
    // Without its own tick the board would still be calling this calm at closing time.
    const callsBefore = client.calls;
    await vi.advanceTimersByTimeAsync(6 * 60_000);

    expect(store.orders()[0].urgency).toBe('late');
    expect(client.calls).toBe(callsBefore);
  });

  // ------------------------------------------------------------------ helpers

  async function created(): Promise<QueueStore> {
    const store = TestBed.inject(QueueStore);
    await settle();
    return store;
  }

  /** Runs the effect and lets the client's promise resolve. */
  async function settle(): Promise<void> {
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(0);
  }

  function row(overrides: {
    id: string;
    status?: OrderStatus;
    minutesAgo?: number;
  }): OrderSummaryResponse {
    return {
      id: overrides.id,
      orderNumber: `FRIESLAB-260903-00${overrides.id}`,
      status: overrides.status ?? OrderStatus.Placed,
      fulfillment: FulfillmentType.Pickup,
      placedAt: new Date(Date.now() - (overrides.minutesAgo ?? 0) * 60_000),
      totalUsd: 23.5,
      itemCount: 2,
      promisedMinutesMin: 20,
      promisedMinutesMax: 30,
      restaurantName: 'FriesLab',
      restaurantSlug: 'frieslab',
      customerName: 'Rita Customer',
      rejectionReason: null,
      // Empty on purpose: these tests are about grouping and urgency. What a card
      // offers is order-actions.spec.ts.
      availableTransitions: [],
    };
  }
});

class FakeOrdersClient {
  calls = 0;
  lastStatuses: OrderStatus[] | undefined;

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

  queue(status?: OrderStatus[]): Observable<PagedResultOfOrderSummaryResponse> {
    this.calls++;
    this.lastStatuses = status;

    if (this.failing) {
      return throwError(() => new Error('the API is not answering'));
    }

    return of({
      items: this.items,
      page: 1,
      pageSize: 50,
      totalCount: this.totalCount,
      totalPages: 1,
      hasNextPage: false,
    } as PagedResultOfOrderSummaryResponse);
  }
}

/** Records what was asked of the move endpoint, which is the whole point of these four tests. */
class FakeMoveClient {
  readonly sent: {
    orderId: string;
    to: OrderStatus;
    reason: RejectionReason | null;
    note: string | null;
  }[] = [];

  private failing = false;

  fails(): void {
    this.failing = true;
  }

  changeStatus(orderId: string, body: ChangeOrderStatusRequest): Observable<OrderDetailResponse> {
    if (this.failing) {
      return throwError(() => new Error('somebody else moved it first'));
    }

    this.sent.push({ orderId, to: body.to, reason: body.reason, note: body.note });
    return of({} as OrderDetailResponse);
  }
}

/** Just enough of the settings client for the pause switch. */
class FakeSettingsClient {
  lastSet: boolean | undefined;

  private accepting = true;
  private failing = false;

  fails(): void {
    this.failing = true;
  }

  get(): Observable<RestaurantSettingsResponse> {
    if (this.failing) {
      return throwError(() => new Error('the API is not answering'));
    }

    return of({ isAcceptingOrders: this.accepting } as RestaurantSettingsResponse);
  }

  setAcceptingOrders(body: { isAcceptingOrders: boolean }): Observable<RestaurantSettingsResponse> {
    this.lastSet = body.isAcceptingOrders;
    this.accepting = body.isAcceptingOrders;

    return of({ isAcceptingOrders: this.accepting } as RestaurantSettingsResponse);
  }
}

class FakeStream {
  readonly revisionSignal: WritableSignal<number> = signal(0);
  readonly statusSignal: WritableSignal<LiveStatus> = signal<LiveStatus>('live');

  readonly revision = this.revisionSignal.asReadonly();
  readonly status = this.statusSignal.asReadonly();
}
