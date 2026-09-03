import { Injectable, computed, inject, signal } from '@angular/core';
import {
  OrderStatus,
  OrderSummaryResponse,
  RestaurantOrdersClient,
  describeError,
} from 'api-client';
import { firstValueFrom } from 'rxjs';

/** The statuses an order can end at. Everything else is still being worked, and lives on the queue. */
export const FINISHED_STATUSES: readonly OrderStatus[] = [
  OrderStatus.Delivered,
  OrderStatus.Rejected,
  OrderStatus.Cancelled,
];

/** The filter chips, in the order somebody would reach for them. */
export const HISTORY_FILTERS: readonly {
  key: string;
  label: string;
  statuses: readonly OrderStatus[];
}[] = [
  { key: 'all', label: 'Everything', statuses: FINISHED_STATUSES },
  { key: 'delivered', label: 'Delivered', statuses: [OrderStatus.Delivered] },
  { key: 'refused', label: 'Refused', statuses: [OrderStatus.Rejected] },
  { key: 'cancelled', label: 'Cancelled', statuses: [OrderStatus.Cancelled] },
];

const PAGE_SIZE = 25;

/**
 * What already happened: the orders this restaurant finished, newest first.
 *
 * <h4>Not live, on purpose</h4>
 *
 * The queue refreshes itself because a kitchen mid-service cannot be asked to press anything. A
 * history is read deliberately, by somebody working out what yesterday looked like, and a list
 * that reordered itself under them mid-scroll would be worse than one that is a minute old.
 * Nothing here subscribes to {@link OrderStream}.
 */
@Injectable()
export class HistoryStore {
  private readonly client = inject(RestaurantOrdersClient);

  private readonly ordersSignal = signal<readonly OrderSummaryResponse[]>([]);
  private readonly filterSignal = signal(HISTORY_FILTERS[0]);
  private readonly pageSignal = signal(1);
  private readonly totalSignal = signal(0);

  readonly orders = this.ordersSignal.asReadonly();
  readonly filter = this.filterSignal.asReadonly();
  readonly page = this.pageSignal.asReadonly();
  readonly total = this.totalSignal.asReadonly();

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalSignal() / PAGE_SIZE)));
  readonly hasPrevious = computed(() => this.pageSignal() > 1);
  readonly hasNext = computed(() => this.pageSignal() < this.totalPages());
  readonly isEmpty = computed(() => !this.loading() && this.ordersSignal().length === 0);

  constructor() {
    void this.load();
  }

  /** Changes what is being looked at, and goes back to the first page — page 4 of a different
   *  filter is not where anybody meant to land. */
  async setFilter(filter: (typeof HISTORY_FILTERS)[number]): Promise<void> {
    this.filterSignal.set(filter);
    this.pageSignal.set(1);
    await this.load();
  }

  async goTo(page: number): Promise<void> {
    this.pageSignal.set(Math.min(Math.max(1, page), this.totalPages()));
    await this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);

    try {
      const result = await firstValueFrom(
        this.client.queue([...this.filterSignal().statuses], true, this.pageSignal(), PAGE_SIZE),
      );

      this.ordersSignal.set(result.items);
      this.totalSignal.set(result.totalCount);
      this.error.set(null);
    } catch (error) {
      // Blanked rather than left stale, unlike the queue. A history that quietly kept showing the
      // previous filter's rows after a failed load would be answering a question nobody asked.
      this.ordersSignal.set([]);
      this.error.set(describeError(error, 'Could not load the history.'));
    } finally {
      this.loading.set(false);
    }
  }
}
