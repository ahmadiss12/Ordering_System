import { OrderStatus, RejectionReason } from 'api-client';
import {
  REJECTION_REASONS,
  eventLabel,
  reasonLabel,
  statusLabel,
  statusTone,
} from './order-wording';

/**
 * How orders read on screen.
 *
 * The interesting failure here is not a wrong word, it is a missing one: a status with no entry
 * renders as `undefined` on a receipt, and a reason with no entry renders as nothing at all. Both
 * are what happens when somebody adds an enum member and the API is the only place that knows.
 */
describe('order wording', () => {
  const everyStatus = Object.values(OrderStatus).filter(
    (v): v is OrderStatus => typeof v === 'number',
  );

  const everyReason = Object.values(RejectionReason).filter(
    (v): v is RejectionReason => typeof v === 'number',
  );

  it('has a label for every status the API can send', () => {
    for (const status of everyStatus) {
      expect(statusLabel(status), `no status label for ${OrderStatus[status]}`).toBeTruthy();
      expect(eventLabel(status), `no event label for ${OrderStatus[status]}`).toBeTruthy();
    }
  });

  it('has a reason for every rejection the API can send', () => {
    // The fallback is deliberate but must never be the answer for a real member: it exists for a
    // reason added to the enum and forgotten here, which this test is what prevents.
    for (const reason of everyReason) {
      expect(reasonLabel(reason), `no label for ${RejectionReason[reason]}`).not.toBe(
        'No reason given',
      );
    }

    expect(REJECTION_REASONS).toHaveLength(everyReason.length);
  });

  it('says where an order is on a chip and what happened in a trail', () => {
    // The distinction the trail needed: "Waiting to be accepted" is true of a state and nonsense
    // as an entry in a list of things somebody did.
    expect(statusLabel(OrderStatus.Placed)).toBe('Waiting to be accepted');
    expect(eventLabel(OrderStatus.Placed)).toBe('Placed');
  });

  it('sorts a status into finished well, finished badly, or still running', () => {
    expect(statusTone(OrderStatus.Delivered)).toBe('done');
    expect(statusTone(OrderStatus.Rejected)).toBe('stopped');
    expect(statusTone(OrderStatus.Cancelled)).toBe('stopped');

    for (const status of [
      OrderStatus.Placed,
      OrderStatus.Accepted,
      OrderStatus.Preparing,
      OrderStatus.ReadyForPickup,
      OrderStatus.OutForDelivery,
    ]) {
      expect(statusTone(status)).toBe('live');
    }
  });

  it('names a reason the way somebody would say it', () => {
    // These are read back to the customer. "OutOfStock" is a database value.
    expect(reasonLabel(RejectionReason.OutOfStock)).toBe("We've run out of something");
  });
});
