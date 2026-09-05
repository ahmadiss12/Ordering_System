import { NextOpening } from 'api-client';
import { availabilityLabel, availabilityNote, availabilityOf, whenOpens } from './opening';

/**
 * How a shut restaurant explains itself.
 *
 * <p>
 * The distinction these pin is the whole reason this file exists: a kitchen outside its hours and
 * one that has paused itself look identical in a database and mean different things to somebody
 * choosing dinner. One is worth waiting twenty minutes for; the other is worth coming back
 * tomorrow for.
 * </p>
 */
describe('availability', () => {
  it('is open only when the hours and the kitchen both agree', () => {
    expect(availabilityOf({ isOpenNow: true, isAcceptingOrders: true })).toBe('open');
  });

  it('separates a kitchen that has paused from one that is shut', () => {
    expect(availabilityOf({ isOpenNow: true, isAcceptingOrders: false })).toBe('paused');
    expect(availabilityOf({ isOpenNow: false, isAcceptingOrders: true })).toBe('closed');
  });

  it('calls a restaurant closed when it is outside its hours, whatever its own switch says', () => {
    // A kitchen that paused itself and then closed for the night is closed. Saying "too busy" at
    // two in the morning would send somebody back in twenty minutes to a locked door.
    expect(availabilityOf({ isOpenNow: false, isAcceptingOrders: false })).toBe('closed');
  });

  it('promises nothing about when a paused kitchen returns', () => {
    const note = availabilityNote('paused', null);

    // Nobody knows when a rush ends, so no time is invented. "Back in 20 minutes" would be a
    // promise the restaurant never made.
    expect(note).not.toMatch(/\d/);
    expect(availabilityLabel('paused')).toBe('Too busy');
  });

  it('says when a closed kitchen opens, when it can', () => {
    expect(availabilityNote('closed', opening(1, '11:00:00', 0))).toBe('Opens at 11:00');
  });

  it('says only that it is closed when there are no hours at all', () => {
    // A kitchen on holiday, which the product allows. Silence about when is the truth.
    expect(availabilityNote('closed', null)).toBe('Closed for now');
  });

  it('says nothing at all about a restaurant that is open', () => {
    expect(availabilityNote('open', null)).toBe('');
  });
});

describe('when a kitchen opens', () => {
  it('leaves today unqualified', () => {
    expect(whenOpens(opening(1, '18:00:00', 0))).toBe('at 18:00');
  });

  it('names tomorrow rather than the weekday', () => {
    // "Opens Tuesday at 09:00" on a Monday night is correct and reads like next week.
    expect(whenOpens(opening(2, '09:00:00', 1))).toBe('tomorrow at 09:00');
  });

  it('names the day when it is further off', () => {
    expect(whenOpens(opening(4, '09:00:00', 3))).toBe('Thursday at 09:00');
  });

  it('trusts the server about which day it is', () => {
    // The same weekday can be today or a week away, and only the server knows what day it is
    // where the kitchen is. A browser in another timezone working it out from the weekday alone
    // would say "tomorrow" about this evening.
    expect(whenOpens(opening(5, '12:00:00', 0))).toBe('at 12:00');
    expect(whenOpens(opening(5, '12:00:00', 7))).toBe('Friday at 12:00');
  });

  it('drops the seconds the API sends', () => {
    expect(whenOpens(opening(1, '09:30:00', 0))).toBe('at 09:30');
  });
});

function opening(day: number, time: string, daysAway: number): NextOpening {
  return { day, time, daysAway } as NextOpening;
}
