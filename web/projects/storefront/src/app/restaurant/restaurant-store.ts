import { Injectable, computed, inject, signal } from '@angular/core';
import { RestaurantDetail, RestaurantMenu, RestaurantsClient, describeError } from 'api-client';
import { firstValueFrom } from 'rxjs';
import { availabilityOf } from '../catalog/opening';

/**
 * One restaurant and its menu.
 *
 * <h4>Both, or neither</h4>
 *
 * The two calls are made together and the screen waits for both. A menu drawn beside a header
 * that has not arrived would show prices with nothing saying whether the kitchen is even open,
 * and somebody would start choosing dinner from a restaurant that shut an hour ago.
 */
@Injectable()
export class RestaurantStore {
  private readonly client = inject(RestaurantsClient);

  private readonly detailSignal = signal<RestaurantDetail | null>(null);
  private readonly menuSignal = signal<RestaurantMenu | null>(null);

  readonly detail = this.detailSignal.asReadonly();
  readonly loading = signal(true);
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);

  /** True when the slug is not one the platform serves — a different failure from a broken load. */
  readonly notFound = signal(false);

  readonly availability = computed(() => {
    const detail = this.detailSignal();
    return detail ? availabilityOf(detail) : 'closed';
  });

  /** Sections with nothing in them are the restaurant's business, not the customer's. */
  readonly categories = computed(
    () => this.menuSignal()?.categories.filter((c) => c.items.length > 0) ?? [],
  );

  readonly isEmpty = computed(() => this.loaded() && this.categories().length === 0);

  async load(slug: string): Promise<void> {
    this.loading.set(true);
    this.notFound.set(false);

    try {
      const [detail, menu] = await Promise.all([
        firstValueFrom(this.client.get(slug)),
        firstValueFrom(this.client.menu(slug)),
      ]);

      this.detailSignal.set(detail);
      this.menuSignal.set(menu);
      this.error.set(null);
      this.loaded.set(true);
    } catch (error) {
      // A slug nobody has is not a failure to be retried — it is a link that has gone, and
      // "try again" would be advice that cannot work.
      if (isNotFound(error)) {
        this.notFound.set(true);
      } else {
        this.error.set(describeError(error, 'Could not load this restaurant.'));
      }
    } finally {
      this.loading.set(false);
    }
  }
}

function isNotFound(error: unknown): boolean {
  return (
    typeof error === 'object' && error !== null && (error as { status?: number }).status === 404
  );
}
