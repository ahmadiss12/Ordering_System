import { OrderStatus, OrderSummaryResponse } from 'api-client';
import { MaterialIcon } from 'material-icons';

/**
 * How much attention a row needs, in the order a kitchen would rank them.
 *
 * Three levels rather than a boolean, because "somebody should look at this soon" and "a customer
 * is about to be let down" are different conversations, and a screen that shouted equally about
 * both would be ignored equally for both.
 */
export type Urgency = 'calm' | 'attention' | 'late';

/** One card on the board: the row as the API sent it, plus what the clock makes of it. */
export interface QueuedOrder {
  readonly order: OrderSummaryResponse;

  /** Whole minutes since it was placed. */
  readonly waitingMinutes: number;

  readonly urgency: Urgency;

  /**
   * Minutes left before the promised window closes; negative once it has passed. Null for an
   * order that has already been handed over, where the promise no longer means anything.
   */
  readonly minutesToPromise: number | null;
}

/** A column on the board. */
export interface QueueColumn {
  readonly key: string;
  readonly title: string;

  /** Typed against the bundled font, so a name it does not carry fails the build. */
  readonly icon: MaterialIcon;
  readonly statuses: readonly OrderStatus[];
  readonly orders: readonly QueuedOrder[];
}

/**
 * How long an unanswered order may sit before the screen starts saying so.
 *
 * Two minutes, then five. A customer watching a spinner has no idea whether anybody has seen
 * their order, and the honest answer for the first two minutes is that somebody is probably
 * plating another one.
 */
export const ANSWER_ATTENTION_MINUTES = 2;
export const ANSWER_LATE_MINUTES = 5;

/**
 * How close to the promised window counts as needing attention. Five minutes is roughly the
 * point where a kitchen can still do something about it.
 */
export const PROMISE_ATTENTION_MINUTES = 5;

/** The statuses a kitchen works. The finished ones belong to the history screen. */
export const LIVE_STATUSES: readonly OrderStatus[] = [
  OrderStatus.Placed,
  OrderStatus.Accepted,
  OrderStatus.Preparing,
  OrderStatus.ReadyForPickup,
  OrderStatus.OutForDelivery,
];

/**
 * The board's columns, in the order a kitchen moves through them.
 *
 * Ready and out for delivery share the last one: they are the same moment in the kitchen's day —
 * the food has left the pass — and splitting them would give every restaurant a permanently
 * empty column, since an order is one or the other and most restaurants lean heavily one way.
 */
export const COLUMNS: readonly Omit<QueueColumn, 'orders'>[] = [
  { key: 'new', title: 'New', icon: 'notifications_active', statuses: [OrderStatus.Placed] },
  { key: 'accepted', title: 'Accepted', icon: 'check_circle', statuses: [OrderStatus.Accepted] },
  { key: 'cooking', title: 'Cooking', icon: 'soup_kitchen', statuses: [OrderStatus.Preparing] },
  {
    key: 'out',
    title: 'Ready & on the way',
    icon: 'local_shipping',
    statuses: [OrderStatus.ReadyForPickup, OrderStatus.OutForDelivery],
  },
];

/**
 * Works out how much attention one order needs, given the time now.
 *
 * `now` is passed in rather than read here so the whole board is judged against one instant — two
 * rows a millisecond apart tipping over a threshold differently would be a flicker nobody could
 * explain — and so a test can decide what time it is.
 */
export function assess(order: OrderSummaryResponse, now: number): QueuedOrder {
  const placedAt = new Date(order.placedAt).getTime();
  const waitingMinutes = Math.max(0, Math.floor((now - placedAt) / 60_000));

  // An order already at the pass has met its promise as far as the kitchen is concerned; keeping
  // a countdown on it would have the board shouting about food that is out of the door.
  const stillCooking =
    order.status === OrderStatus.Placed ||
    order.status === OrderStatus.Accepted ||
    order.status === OrderStatus.Preparing;

  const minutesToPromise = stillCooking ? order.promisedMinutesMax - waitingMinutes : null;

  return {
    order,
    waitingMinutes,
    urgency: urgencyOf(order, waitingMinutes, minutesToPromise),
    minutesToPromise,
  };
}

function urgencyOf(
  order: OrderSummaryResponse,
  waitingMinutes: number,
  minutesToPromise: number | null,
): Urgency {
  // An unanswered order is its own kind of late. Nobody has even said yes to the customer yet,
  // so this is measured from placement rather than against the promise.
  if (order.status === OrderStatus.Placed) {
    if (waitingMinutes >= ANSWER_LATE_MINUTES) {
      return 'late';
    }
    if (waitingMinutes >= ANSWER_ATTENTION_MINUTES) {
      return 'attention';
    }
  }

  if (minutesToPromise !== null) {
    if (minutesToPromise < 0) {
      return 'late';
    }
    if (minutesToPromise <= PROMISE_ATTENTION_MINUTES) {
      return 'attention';
    }
  }

  return 'calm';
}
