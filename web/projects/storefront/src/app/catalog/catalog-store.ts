import { Injectable, computed, inject, signal } from '@angular/core';
import { RestaurantSummary, RestaurantsClient, describeError } from 'api-client';
import { firstValueFrom } from 'rxjs';
import { availabilityOf } from './opening';

/**
 * Everywhere a customer could order from.
 *
 * <h4>Open ones first</h4>
 *
 * The server orders by name, because whether a kitchen is open is worked out after the page is
 * cut and cannot be sorted on in SQL. So the ordering happens here — which is honest only because
 * the whole list fits on one page today. The day it does not, this becomes a lie about the second
 * page and the sorting has to move to the server.
 */
@Injectable()
export class CatalogStore {
  private readonly client = inject(RestaurantsClient);

  private readonly allSignal = signal<readonly RestaurantSummary[]>([]);

  readonly loading = signal(true);
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);

  readonly restaurants = computed(() => {
    const rank = { open: 0, paused: 1, closed: 2 } as const;

    return [...this.allSignal()].sort(
      (a, b) => rank[availabilityOf(a)] - rank[availabilityOf(b)] || a.name.localeCompare(b.name),
    );
  });

  readonly openCount = computed(
    () => this.allSignal().filter((r) => availabilityOf(r) === 'open').length,
  );

  readonly isEmpty = computed(() => this.loaded() && this.allSignal().length === 0);

  async load(): Promise<void> {
    this.loading.set(true);

    try {
      const page = await firstValueFrom(this.client.list(undefined, undefined, undefined));

      this.allSignal.set(page.items);
      this.error.set(null);
      this.loaded.set(true);
    } catch (error) {
      this.error.set(describeError(error, 'Could not load the restaurants.'));
    } finally {
      this.loading.set(false);
    }
  }
}
