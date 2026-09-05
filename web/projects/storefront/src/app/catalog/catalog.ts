import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { RestaurantSummary } from 'api-client';
import { Availability, availabilityLabel, availabilityNote, availabilityOf } from './opening';
import { CatalogStore } from './catalog-store';

/**
 * Where a customer starts: everywhere they could order from.
 *
 * <h4>Shut restaurants stay on the list</h4>
 *
 * Hiding them would be tidier and worse. Somebody looking for the place they had last week needs
 * to find it and be told when it opens, not be left wondering whether it has closed for good —
 * and a list that shrinks after dark looks broken rather than accurate.
 */
@Component({
  selector: 'app-catalog',
  providers: [CatalogStore],
  imports: [RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule],
  templateUrl: './catalog.html',
  styleUrl: './catalog.scss',
})
export class Catalog {
  protected readonly store = inject(CatalogStore);

  constructor() {
    void this.store.load();
  }

  protected availability(restaurant: RestaurantSummary): Availability {
    return availabilityOf(restaurant);
  }

  protected label(restaurant: RestaurantSummary): string {
    return availabilityLabel(this.availability(restaurant));
  }

  protected note(restaurant: RestaurantSummary): string {
    return availabilityNote(this.availability(restaurant), restaurant.nextOpening);
  }

  protected minimum(restaurant: RestaurantSummary): string {
    return restaurant.minOrderUsd > 0
      ? `$${restaurant.minOrderUsd.toFixed(2)} minimum`
      : 'No minimum';
  }
}
