import { FulfillmentType, OrderStatus } from 'api-client';
import { MaterialIcon } from 'material-icons';

/**
 * One button on a card: where it takes the order, and how it should read.
 */
export interface OrderAction {
  readonly to: OrderStatus;
  readonly label: string;
  readonly icon: MaterialIcon;

  /**
   * The move a kitchen makes nine times out of ten. Drawn as the filled button; everything else
   * sits beside it as an outline, so the common action is the one a thumb finds without looking.
   */
  readonly primary: boolean;

  /** Whether pressing it opens a form. True for the moves the state machine refuses without one. */
  readonly needsReason: boolean;
}

/**
 * How each move should read on a button.
 *
 * Keyed by status alone, not by column, because a card is told which moves it may make by the API
 * — `availableTransitions`, straight from the transition table — and this only decides the words.
 * Deriving the buttons from the column instead would be a second copy of the table, and the first
 * copy is the one that decides whether the API accepts the press.
 *
 * Delivered reads differently for pickup and delivery, which is the one place the wording depends
 * on more than the status: "Collected" is what happened at a counter, and "Delivered" is what
 * happened at a door.
 */
const WORDING: Record<OrderStatus, Omit<OrderAction, 'to'> | null> = {
  [OrderStatus.Accepted]: {
    label: 'Accept',
    icon: 'check',
    primary: true,
    needsReason: false,
  },
  [OrderStatus.Rejected]: {
    label: 'Refuse',
    icon: 'block',
    primary: false,
    // A rejection is what the rejection-rate report counts, and it cannot group by a sentence.
    needsReason: true,
  },
  [OrderStatus.Preparing]: {
    label: 'Start cooking',
    icon: 'soup_kitchen',
    primary: true,
    needsReason: false,
  },
  [OrderStatus.ReadyForPickup]: {
    label: 'Ready',
    icon: 'room_service',
    primary: true,
    needsReason: false,
  },
  [OrderStatus.OutForDelivery]: {
    label: 'Send out',
    icon: 'moped',
    primary: true,
    needsReason: false,
  },
  [OrderStatus.Delivered]: {
    label: 'Delivered',
    icon: 'done_all',
    primary: true,
    needsReason: false,
  },
  [OrderStatus.Cancelled]: {
    label: "Can't complete",
    icon: 'cancel',
    primary: false,
    // A restaurant backing out of an order it accepted lands in the same report a rejection does.
    needsReason: true,
  },

  // Nothing moves an order back to Placed. It appears here because the map is keyed by the whole
  // enum, which is what makes adding a status a compile error rather than a silently blank button.
  [OrderStatus.Placed]: null,
};

/**
 * The buttons for one order, in the order they should be drawn.
 *
 * Takes the moves the API said were available rather than working them out, so a screen can never
 * offer something the server would refuse — and never hides something it would allow.
 */
export function actionsFor(
  available: readonly OrderStatus[],
  fulfillment: FulfillmentType,
): readonly OrderAction[] {
  return (
    available
      .map((to) => {
        const wording = WORDING[to];
        return wording ? { to, ...wording, label: labelFor(to, fulfillment, wording.label) } : null;
      })
      .filter((action): action is OrderAction => action !== null)
      // The one a thumb should find first, first.
      .sort((a, b) => Number(b.primary) - Number(a.primary))
  );
}

function labelFor(to: OrderStatus, fulfillment: FulfillmentType, fallback: string): string {
  if (to !== OrderStatus.Delivered) {
    return fallback;
  }

  return fulfillment === FulfillmentType.Pickup ? 'Collected' : 'Delivered';
}
