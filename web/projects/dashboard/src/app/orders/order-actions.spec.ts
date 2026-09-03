import { FulfillmentType, OrderStatus } from 'api-client';
import { actionsFor } from './order-actions';

/**
 * What a card offers, and — as much — what it does not.
 *
 * The board never works out which moves are legal; the API says so, from the transition table.
 * What is decided here is only the wording and the ordering, so these tests are about a screen
 * that reads correctly rather than about the rules.
 */
describe('actionsFor', () => {
  it('draws only what the API said was available', () => {
    const actions = actionsFor(
      [OrderStatus.Accepted, OrderStatus.Rejected],
      FulfillmentType.Pickup,
    );

    expect(actions.map((a) => a.to)).toEqual([OrderStatus.Accepted, OrderStatus.Rejected]);
  });

  it('offers nothing on a finished order', () => {
    // Delivered, rejected and cancelled come back with an empty list, and an empty list must draw
    // no buttons rather than falling back to a default set.
    expect(actionsFor([], FulfillmentType.Pickup)).toEqual([]);
  });

  it('puts the common move first', () => {
    // A kitchen presses Accept a hundred times for every Refuse, so it is the one a thumb should
    // find without reading.
    const actions = actionsFor(
      [OrderStatus.Rejected, OrderStatus.Accepted],
      FulfillmentType.Pickup,
    );

    expect(actions[0].to).toBe(OrderStatus.Accepted);
    expect(actions[0].primary).toBe(true);
    expect(actions[1].primary).toBe(false);
  });

  it('asks for a reason on exactly the moves the state machine refuses without one', () => {
    const needing = actionsFor(
      [OrderStatus.Accepted, OrderStatus.Rejected, OrderStatus.Preparing, OrderStatus.Cancelled],
      FulfillmentType.Pickup,
    )
      .filter((a) => a.needsReason)
      .map((a) => a.to);

    // Refusing an order and backing out of one already accepted are what the rejection-rate
    // report counts. Accepting and cooking are not.
    expect(needing).toEqual(expect.arrayContaining([OrderStatus.Rejected, OrderStatus.Cancelled]));
    expect(needing).toHaveLength(2);
  });

  it('says collected at a counter and delivered at a door', () => {
    // The one place the wording depends on more than the status. Telling a pickup customer their
    // order was "delivered" is a small lie a kitchen would have to explain on the phone.
    const pickup = actionsFor([OrderStatus.Delivered], FulfillmentType.Pickup);
    const delivery = actionsFor([OrderStatus.Delivered], FulfillmentType.Delivery);

    expect(pickup[0].label).toBe('Collected');
    expect(delivery[0].label).toBe('Delivered');
  });

  it('has wording for every status the transition table can offer', () => {
    // A status with no entry draws no button, which on this screen looks like an order that
    // cannot be moved on rather than like a gap in a lookup table.
    const everyMove = [
      OrderStatus.Accepted,
      OrderStatus.Rejected,
      OrderStatus.Preparing,
      OrderStatus.ReadyForPickup,
      OrderStatus.OutForDelivery,
      OrderStatus.Delivered,
      OrderStatus.Cancelled,
    ];

    expect(actionsFor(everyMove, FulfillmentType.Delivery)).toHaveLength(everyMove.length);
  });
});
