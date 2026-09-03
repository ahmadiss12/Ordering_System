import { FulfillmentType, OrderStatus, OrderSummaryResponse } from 'api-client';
import { COLUMNS, LIVE_STATUSES, assess } from './queue-model';

/**
 * What the board decides is urgent.
 *
 * These are the rules a member of staff reads off the screen while deciding what to do next, so
 * getting them wrong is worse than having no colour at all: a board that cries wolf is a board
 * nobody looks at, and one that stays calm through a breach is one nobody can rely on.
 */
describe('assess', () => {
  const now = new Date('2026-09-03T20:00:00Z').getTime();

  // ------------------------------------------------------------------ waiting

  it('counts whole minutes since the order was placed', () => {
    const row = assess(order({ minutesAgo: 7 }), now);

    expect(row.waitingMinutes).toBe(7);
  });

  it('never reports a negative wait', () => {
    // Clocks disagree. A tablet a few seconds ahead of the server must not show "-1 min ago".
    const row = assess(order({ minutesAgo: -2 }), now);

    expect(row.waitingMinutes).toBe(0);
  });

  // ------------------------------------------------------------------ an unanswered order

  it('leaves a brand-new order alone', () => {
    // Somebody is probably plating another one. Shouting after thirty seconds trains staff to
    // ignore the colour.
    const row = assess(order({ minutesAgo: 1, status: OrderStatus.Placed }), now);

    expect(row.urgency).toBe('calm');
  });

  it('asks for attention on an order nobody has answered in two minutes', () => {
    const row = assess(order({ minutesAgo: 2, status: OrderStatus.Placed }), now);

    expect(row.urgency).toBe('attention');
  });

  it('calls an order late when nobody has answered it in five', () => {
    const row = assess(order({ minutesAgo: 5, status: OrderStatus.Placed }), now);

    expect(row.urgency).toBe('late');
  });

  // ------------------------------------------------------------------ the promise

  it('counts down to the promised time', () => {
    const row = assess(
      order({ minutesAgo: 10, status: OrderStatus.Preparing, promisedMax: 40 }),
      now,
    );

    expect(row.minutesToPromise).toBe(30);
    expect(row.urgency).toBe('calm');
  });

  it('asks for attention as the promise gets close', () => {
    const row = assess(
      order({ minutesAgo: 36, status: OrderStatus.Preparing, promisedMax: 40 }),
      now,
    );

    expect(row.minutesToPromise).toBe(4);
    expect(row.urgency).toBe('attention');
  });

  it('calls an order late once the promise has passed', () => {
    const row = assess(
      order({ minutesAgo: 45, status: OrderStatus.Preparing, promisedMax: 40 }),
      now,
    );

    expect(row.minutesToPromise).toBe(-5);
    expect(row.urgency).toBe('late');
  });

  it('stops counting once the food has left the pass', () => {
    // The kitchen has done its part. A countdown still running on an order out for delivery
    // would have the board shouting about food already on a moped.
    const row = assess(
      order({ minutesAgo: 90, status: OrderStatus.OutForDelivery, promisedMax: 40 }),
      now,
    );

    expect(row.minutesToPromise).toBeNull();
    expect(row.urgency).toBe('calm');
  });

  // ------------------------------------------------------------------ the columns

  it('gives every live status a column to appear in', () => {
    // A status with no column is an order that vanishes off the board while still being cooked,
    // which is the one failure this screen must not have.
    const covered = COLUMNS.flatMap((column) => column.statuses);

    for (const status of LIVE_STATUSES) {
      expect(covered).toContain(status);
    }
  });

  it('never puts one order in two columns', () => {
    const covered = COLUMNS.flatMap((column) => column.statuses);

    expect(new Set(covered).size).toBe(covered.length);
  });

  function order(overrides: {
    minutesAgo: number;
    status?: OrderStatus;
    promisedMax?: number;
  }): OrderSummaryResponse {
    return {
      id: '11111111-1111-1111-1111-111111111111',
      orderNumber: 'FRIESLAB-260903-001',
      status: overrides.status ?? OrderStatus.Placed,
      fulfillment: FulfillmentType.Pickup,
      placedAt: new Date(now - overrides.minutesAgo * 60_000),
      totalUsd: 23.5,
      itemCount: 2,
      promisedMinutesMin: (overrides.promisedMax ?? 30) - 10,
      promisedMinutesMax: overrides.promisedMax ?? 30,
      restaurantName: 'FriesLab',
      restaurantSlug: 'frieslab',
      customerName: 'Rita Customer',
    };
  }
});
