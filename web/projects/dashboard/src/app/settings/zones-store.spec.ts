import { TestBed } from '@angular/core/testing';
import { RestaurantZoneResponse, RestaurantZonesClient } from 'api-client';
import { Observable, of, throwError } from 'rxjs';
import { ZonesStore } from './zones-store';

/**
 * The zone grid.
 *
 * Each row is independent, so what is worth testing is that they stay that way: one row's edit
 * does not touch another, one row's failure does not lose another's work, and a row that has
 * never been configured does not arrive claiming free delivery in no time at all.
 */
describe('ZonesStore', () => {
  let client: FakeZonesClient;

  beforeEach(() => {
    client = new FakeZonesClient();
    TestBed.configureTestingModule({
      providers: [ZonesStore, { provide: RestaurantZonesClient, useValue: client }],
    });
  });

  it('gives an unconfigured zone something plausible rather than nothing', async () => {
    client.returns([zone('a', 'Hamra', false, null, null)]);
    const store = await loaded();

    // Free delivery in no time at all is a promise nobody meant to make by pressing one switch.
    const row = store.rows()[0];
    expect(row.feeUsd).toBeGreaterThan(0);
    expect(row.minutes).toBeGreaterThan(0);

    // And it is not dirty yet: nothing has been changed, only shown.
    expect(store.isDirty(row)).toBe(false);
  });

  it('keeps the terms a suspended zone remembers', async () => {
    client.returns([zone('a', 'Jounieh', false, 6, 45)]);
    const store = await loaded();

    // The row is off but the numbers are the restaurant's own, which is what makes turning it
    // back on one press.
    expect(store.rows()[0].feeUsd).toBe(6);
    expect(store.rows()[0].minutes).toBe(45);
  });

  it('only marks the row somebody changed', async () => {
    client.returns([zone('a', 'Hamra', true, 2, 20), zone('b', 'Achrafieh', true, 3, 25)]);
    const store = await loaded();

    store.update('a', { feeUsd: 4 });

    expect(store.isDirty(store.rows()[0])).toBe(true);
    expect(store.isDirty(store.rows()[1])).toBe(false);
  });

  it('sends one zone and leaves the rest alone', async () => {
    client.returns([zone('a', 'Hamra', true, 2, 20), zone('b', 'Achrafieh', true, 3, 25)]);
    const store = await loaded();

    store.update('a', { isServed: false });
    store.update('b', { feeUsd: 9 });

    await store.save('a');

    expect(client.sent).toEqual([
      { zoneId: 'a', isServed: false, deliveryFeeUsd: 2, estimatedMinutes: 20 },
    ]);
    // The other row's edit is still there, unsent and unsaved.
    expect(store.isDirty(store.rows()[1])).toBe(true);
  });

  it('names the zone that failed and keeps its edit', async () => {
    client.returns([zone('a', 'Hamra', true, 2, 20)]);
    const store = await loaded();

    store.update('a', { feeUsd: 4 });
    client.fails();

    expect(await store.save('a')).toBe(false);
    // A grid of ten rows with one failure has to say which one.
    expect(store.error()).toContain('Hamra');
    expect(store.rows()[0].feeUsd).toBe(4);
  });

  it('puts one row back without touching another', async () => {
    client.returns([zone('a', 'Hamra', true, 2, 20), zone('b', 'Achrafieh', true, 3, 25)]);
    const store = await loaded();

    store.update('a', { feeUsd: 4 });
    store.update('b', { feeUsd: 9 });

    store.discard('a');

    expect(store.rows()[0].feeUsd).toBe(2);
    expect(store.rows()[1].feeUsd).toBe(9);
  });

  it('stops claiming a row is dirty once it saves', async () => {
    client.returns([zone('a', 'Hamra', true, 2, 20)]);
    const store = await loaded();

    store.update('a', { feeUsd: 4 });
    await store.save('a');

    expect(store.isDirty(store.rows()[0])).toBe(false);
  });

  it('does not pretend to know the zones when the load failed', async () => {
    client.fails();
    const store = TestBed.inject(ZonesStore);
    await store.load();

    // An empty grid would read as "you deliver nowhere", which is a confident answer to a
    // question it could not answer.
    expect(store.loaded()).toBe(false);
    expect(store.error()).not.toBeNull();
  });

  async function loaded(): Promise<ZonesStore> {
    const store = TestBed.inject(ZonesStore);
    await store.load();
    return store;
  }

  function zone(
    zoneId: string,
    zoneName: string,
    isServed: boolean,
    deliveryFeeUsd: number | null,
    estimatedMinutes: number | null,
  ): RestaurantZoneResponse {
    return {
      zoneId,
      zoneName,
      isServed,
      deliveryFeeUsd,
      estimatedMinutes,
    } as RestaurantZoneResponse;
  }
});

class FakeZonesClient {
  readonly sent: {
    zoneId: string;
    isServed: boolean;
    deliveryFeeUsd: number;
    estimatedMinutes: number;
  }[] = [];

  private zones: RestaurantZoneResponse[] = [];
  private failing = false;

  returns(zones: RestaurantZoneResponse[]): void {
    this.zones = zones;
  }

  fails(): void {
    this.failing = true;
  }

  list(): Observable<RestaurantZoneResponse[]> {
    if (this.failing) {
      return throwError(() => new Error('the API is not answering'));
    }

    return of(this.zones);
  }

  set(
    zoneId: string,
    body: { isServed: boolean; deliveryFeeUsd: number; estimatedMinutes: number },
  ): Observable<RestaurantZoneResponse> {
    if (this.failing) {
      return throwError(() => new Error('the API is not answering'));
    }

    this.sent.push({ zoneId, ...body });

    return of({
      zoneId,
      zoneName: this.zones.find((z) => z.zoneId === zoneId)?.zoneName ?? '',
      isServed: body.isServed,
      deliveryFeeUsd: body.deliveryFeeUsd,
      estimatedMinutes: body.estimatedMinutes,
    } as RestaurantZoneResponse);
  }
}
