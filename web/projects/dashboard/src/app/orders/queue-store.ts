import { DestroyRef, Injectable, computed, effect, inject, signal } from '@angular/core';
import { OrderSummaryResponse, RestaurantOrdersClient, describeError } from 'api-client';
import { OrderStream } from 'realtime';
import { firstValueFrom } from 'rxjs';
import { COLUMNS, LIVE_STATUSES, QueueColumn, QueuedOrder, assess } from './queue-model';

/** The server's own cap. Asking for more would be clamped to this anyway. */
const PAGE_SIZE = 50;

/**
 * How often the board re-judges itself against the clock. Ten seconds is under the granularity
 * of everything it displays — whole minutes — so nothing is ever more than a tick out of date,
 * and it costs one signal write.
 */
const TICK_MS = 10_000;

/**
 * The board a kitchen works from during service.
 *
 * <h4>One way in</h4>
 *
 * It refetches whenever {@link OrderStream}'s revision changes, and that is the only trigger it
 * has. The stream already folds a pushed message, a reconnection and the poll behind it into that
 * one number, so this store never asks whether an order arrived over a socket or was noticed by a
 * timer — and has no second code path that could disagree with the first.
 *
 * <h4>Why it holds a clock of its own</h4>
 *
 * Urgency is a comparison against the time now, so the board has to re-judge itself even when
 * nothing has changed on the server: an order sitting unanswered becomes late without anybody
 * touching it. Without this tick, a screen nobody reloads would keep showing an order as calm
 * twenty minutes after it stopped being calm.
 *
 * Provided by the route rather than in root, so leaving the screen drops the orders and stops the
 * timer instead of holding a copy that quietly ages.
 */
@Injectable()
export class QueueStore {
  private readonly client = inject(RestaurantOrdersClient);
  private readonly stream = inject(OrderStream);

  private readonly ordersSignal = signal<readonly OrderSummaryResponse[]>([]);
  private readonly totalSignal = signal(0);
  private readonly nowSignal = signal(Date.now());

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /** Whether the live channel is up. Shown, not decided with: the board refreshes either way. */
  readonly live = computed(() => this.stream.status() === 'live');

  /** Every live order, judged against the current tick. */
  readonly orders = computed<readonly QueuedOrder[]>(() => {
    const now = this.nowSignal();
    return this.ordersSignal().map((order) => assess(order, now));
  });

  readonly columns = computed<readonly QueueColumn[]>(() => {
    const all = this.orders();
    return COLUMNS.map((column) => ({
      ...column,
      // Already oldest-first from the server, which is the order a queue is worked in, so the
      // rows keep the order they arrived in rather than being re-sorted into a second opinion.
      orders: all.filter((q) => column.statuses.includes(q.order.status)),
    }));
  });

  readonly shown = computed(() => this.ordersSignal().length);

  /** How many live orders exist, which can exceed {@link shown} on a page cap of fifty. */
  readonly total = this.totalSignal.asReadonly();

  /** True when the board is not showing everything, so it can say so rather than mislead. */
  readonly truncated = computed(() => this.totalSignal() > this.ordersSignal().length);

  /** How many rows want attention, for a heading readable from across a kitchen. */
  readonly needingAttention = computed(
    () => this.orders().filter((q) => q.urgency !== 'calm').length,
  );

  readonly isEmpty = computed(() => !this.loading() && this.ordersSignal().length === 0);

  constructor() {
    // Reading revision() is what subscribes this effect to it, so the first run both registers
    // the dependency and loads the board. There is deliberately no separate initial load.
    effect(() => {
      this.stream.revision();
      void this.refresh();
    });

    const ticker = setInterval(() => this.nowSignal.set(Date.now()), TICK_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(ticker));
  }

  /** Re-reads the queue. Called by the effect above, and by a person pressing refresh. */
  async refresh(): Promise<void> {
    try {
      const page = await firstValueFrom(this.client.queue([...LIVE_STATUSES], 1, PAGE_SIZE));

      this.ordersSignal.set(page.items);
      this.totalSignal.set(page.totalCount);
      this.error.set(null);
    } catch (error) {
      // The rows already on screen are left alone. A kitchen mid-service is far better off with
      // a board that is a few seconds stale and says so than with an empty one.
      this.error.set(describeError(error, 'Could not refresh the queue.'));
    } finally {
      this.loading.set(false);
    }
  }
}
