import { OrderStatus, RejectionReason } from 'api-client';

/**
 * How an order's status and its refusal read on screen, in one place.
 *
 * The API has wording of its own — `OrderStateMachine.Describe` — and this is deliberately a
 * second copy rather than something sent down with the order. Wording is not a rule: the same
 * status is "Refuse" on a button, "Refused" on a chip and "rejected" inside a server sentence,
 * and a single string shipped from the API would be wrong in two of those three places.
 *
 * What must never be duplicated is which moves are legal, and that is not here. It comes from the
 * transition table, through `availableTransitions`, and nothing on this screen second-guesses it.
 */

/** The label on a chip or a heading. Past tense, because it says where the order got to. */
export function statusLabel(status: OrderStatus): string {
  return STATUS_LABELS[status];
}

const STATUS_LABELS: Record<OrderStatus, string> = {
  [OrderStatus.Placed]: 'Waiting to be accepted',
  [OrderStatus.Accepted]: 'Accepted',
  [OrderStatus.Preparing]: 'Being cooked',
  [OrderStatus.ReadyForPickup]: 'Ready for pickup',
  [OrderStatus.OutForDelivery]: 'Out for delivery',
  [OrderStatus.Delivered]: 'Delivered',
  [OrderStatus.Rejected]: 'Refused',
  [OrderStatus.Cancelled]: 'Cancelled',
};

/**
 * The same status as a line in the trail: what happened, not where the order is.
 *
 * "Waiting to be accepted" is a true description of an order's state and a nonsense entry in a
 * list of events — the trail is read as a sequence of things somebody did, and the first line of
 * it is the customer placing the order.
 */
export function eventLabel(status: OrderStatus): string {
  return EVENT_LABELS[status];
}

const EVENT_LABELS: Record<OrderStatus, string> = {
  [OrderStatus.Placed]: 'Placed',
  [OrderStatus.Accepted]: 'Accepted',
  [OrderStatus.Preparing]: 'Started cooking',
  [OrderStatus.ReadyForPickup]: 'Ready for pickup',
  [OrderStatus.OutForDelivery]: 'Sent out for delivery',
  [OrderStatus.Delivered]: 'Handed over',
  [OrderStatus.Rejected]: 'Refused',
  [OrderStatus.Cancelled]: 'Cancelled',
};

/**
 * How the status should be coloured.
 *
 * Three tones rather than eight colours: a screen where every status has its own hue is a screen
 * where none of them mean anything. Finished well, finished badly, still running.
 */
export type StatusTone = 'live' | 'done' | 'stopped';

export function statusTone(status: OrderStatus): StatusTone {
  if (status === OrderStatus.Delivered) {
    return 'done';
  }

  return status === OrderStatus.Rejected || status === OrderStatus.Cancelled ? 'stopped' : 'live';
}

/**
 * The fixed list a restaurant chooses from when it drops an order, in the order a kitchen would
 * reach for them.
 *
 * Written in the first person, because these are read back to the customer. "Out of stock" is a
 * database value; "we've run out of something" is what somebody would actually say.
 */
export const REJECTION_REASONS: readonly { value: RejectionReason; label: string }[] = [
  { value: RejectionReason.OutOfStock, label: "We've run out of something" },
  { value: RejectionReason.TooBusy, label: "We're too busy right now" },
  { value: RejectionReason.ClosingSoon, label: "We're closing soon" },
  { value: RejectionReason.OutsideDeliveryArea, label: "We don't deliver there" },
  { value: RejectionReason.CustomerUnreachable, label: "We can't reach the customer" },
  { value: RejectionReason.Other, label: 'Something else' },
];

/** The chosen reason, for a screen showing why an order was dropped. */
export function reasonLabel(reason: RejectionReason): string {
  // The list above is the single source; falling back to the raw name would put OutsideDeliveryArea
  // on a screen a customer reads, so a reason added to the enum and forgotten here shows as a
  // plain full stop rather than as an identifier.
  return REJECTION_REASONS.find((r) => r.value === reason)?.label ?? 'No reason given';
}
