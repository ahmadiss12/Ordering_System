import { OrderStatus } from 'api-client';

/**
 * What the hub pushes when an order moves.
 *
 * Hand-written, unlike every other contract in this workspace: SignalR is not described by
 * OpenAPI, so there is no generated type for this and no build failure if the two sides drift.
 * A test on the API side reads the raw payload off the wire and asserts these exact property
 * names, because the failure mode here is not an error — it is `undefined` on a screen.
 */
export interface OrderChanged {
  orderId: string;
  orderNumber: string;
  status: OrderStatus;

  /** Null when the order has just been placed and came from nowhere. */
  previousStatus: OrderStatus | null;

  /** ISO-8601, as sent. Left as a string because nothing here needs it as a Date. */
  at: string;
}

/** Where the live channel currently stands, for a screen that wants to say so. */
export type LiveStatus = 'off' | 'connecting' | 'live' | 'reconnecting';
