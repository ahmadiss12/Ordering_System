import { NextOpening } from 'api-client';

/**
 * Whether a customer can order from a restaurant right now, and how to say why not.
 *
 * <h4>Three different noes</h4>
 *
 * The platform can switch a restaurant off — those never reach a customer at all, so there is no
 * wording for them here. The other two look alike in a database and mean quite different things
 * to somebody choosing dinner: a kitchen outside its hours will open again at a time it can name,
 * and one that has paused itself is open but swamped and will be back when it is back. Collapsing
 * them into "Closed" would tell somebody to give up on a restaurant that is twenty minutes from
 * taking orders again.
 */
export type Availability = 'open' | 'paused' | 'closed';

export function availabilityOf(restaurant: {
  isOpenNow: boolean;
  isAcceptingOrders: boolean;
}): Availability {
  if (!restaurant.isOpenNow) {
    return 'closed';
  }

  return restaurant.isAcceptingOrders ? 'open' : 'paused';
}

/** The short badge on a card. */
export function availabilityLabel(availability: Availability): string {
  switch (availability) {
    case 'open':
      return 'Open';
    case 'paused':
      return 'Too busy';
    case 'closed':
      return 'Closed';
  }
}

/**
 * The sentence under the badge.
 *
 * <p>
 * A paused kitchen is given no time, because nobody knows one — inventing "back in 20 minutes"
 * would be a promise the restaurant never made.
 * </p>
 */
export function availabilityNote(
  availability: Availability,
  nextOpening: NextOpening | null | undefined,
): string {
  switch (availability) {
    case 'open':
      return '';
    case 'paused':
      return 'Not taking orders just now — try again shortly';
    case 'closed':
      return nextOpening ? `Opens ${whenOpens(nextOpening)}` : 'Closed for now';
  }
}

/** Indexed by DayOfWeek, which numbers Sunday zero. */
const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/**
 * "at 11:00", "tomorrow at 11:00", "Thursday at 11:00".
 *
 * <p>
 * The day count comes from the server, because only the server knows what day it is where the
 * kitchen is. A browser in another timezone working it out from the weekday alone would say
 * "tomorrow" about this evening.
 * </p>
 */
export function whenOpens(next: NextOpening): string {
  const at = `at ${hourAndMinute(next.time)}`;

  if (next.daysAway === 0) {
    return at;
  }

  if (next.daysAway === 1) {
    return `tomorrow ${at}`;
  }

  return `${DAY_NAMES[next.day]} ${at}`;
}

/** "09:00:00" from the API, "09:00" on screen. Seconds are never part of an opening time. */
export function hourAndMinute(time: string): string {
  return time.slice(0, 5);
}
