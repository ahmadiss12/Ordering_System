import { Component, effect, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { CatalogOpeningWindow } from 'api-client';
import { availabilityLabel, hourAndMinute } from '../catalog/opening';
import { RestaurantStore } from './restaurant-store';

/** Monday first: a week of opening hours is read starting there, not on Sunday as the enum numbers it. */
const WEEK = [
  { day: 1, label: 'Monday' },
  { day: 2, label: 'Tuesday' },
  { day: 3, label: 'Wednesday' },
  { day: 4, label: 'Thursday' },
  { day: 5, label: 'Friday' },
  { day: 6, label: 'Saturday' },
  { day: 0, label: 'Sunday' },
];

/**
 * One restaurant: whether it is open, what it costs to get here, and what is on.
 *
 * <h4>Nothing can be ordered yet</h4>
 *
 * The basket is Step 4. Until then a dish shows its name, its price and whether it is sold out —
 * and no button, because a button that did nothing would be worse than none. What this screen
 * has to get right first is the part somebody decides on: is it open, does it come to me, and how
 * much is the food.
 */
@Component({
  selector: 'app-restaurant',
  providers: [RestaurantStore],
  imports: [
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatDividerModule,
    MatIconModule,
    MatProgressBarModule,
  ],
  templateUrl: './restaurant.html',
  styleUrl: './restaurant.scss',
})
export class Restaurant {
  /** From the route, bound by withComponentInputBinding. */
  readonly slug = input.required<string>();

  protected readonly store = inject(RestaurantStore);
  protected readonly week = WEEK;

  constructor() {
    // An effect rather than a constructor call: the slug arrives as an input, and the router
    // reuses this component when one restaurant links to another.
    effect(() => {
      void this.store.load(this.slug());
    });
  }

  protected label(): string {
    return availabilityLabel(this.store.availability());
  }

  protected money(usd: number): string {
    return `$${usd.toFixed(2)}`;
  }

  /** Every window on one weekday, as "12:00 – 16:00", or nothing when the kitchen is shut. */
  protected windowsOn(day: number): string {
    const windows = (this.store.detail()?.hours ?? []).filter((w) => w.dayOfWeek === day);

    return windows.length === 0 ? '' : windows.map(readable).join(', ');
  }
}

function readable(window: CatalogOpeningWindow): string {
  // A day with no closing time, as the domain writes it. "00:00 – 23:59" would be a different
  // promise, and reading it back that way after an owner chose "all day" would be a small lie.
  if (window.openTime.startsWith('00:00') && window.closeTime.startsWith('23:59:59')) {
    return 'All day';
  }

  return `${hourAndMinute(window.openTime)} – ${hourAndMinute(window.closeTime)}`;
}
