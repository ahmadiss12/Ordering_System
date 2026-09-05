import { DestroyRef, Injectable, computed, effect, inject, signal } from '@angular/core';
import {
  MyOrdersClient,
  OrderStatus,
  OrderSummaryResponse,
  RejectionReason,
  RestaurantOrdersClient,
  RestaurantSettingsClient,
  describeError,
} from 'api-client';
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

  // Moving an order goes through the endpoint that serves both parties, which is where the state
  // machine decides who may make which move. There is no kitchen-only version of it to call.
  private readonly moveClient = inject(MyOrdersClient);
  private readonly stream = inject(OrderStream);

  // Pausing orders is on this screen rather than only in Settings, which is owner-only: the
  // person who needs it is a cook in the middle of service, and making them navigate away from
  // the board to find it is the difference between using it and not.
  private readonly settingsClient = inject(RestaurantSettingsClient);

  private readonly ordersSignal = signal<readonly OrderSummaryResponse[]>([]);
  private readonly totalSignal = signal(0);
  private readonly nowSignal = signal(Date.now());

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /**
   * The order a press is currently in flight for, or null.
   *
   * One at a time and by id, so a card disables its own buttons while its move is happening
   * without freezing the rest of the board — a kitchen accepting one order should not have to
   * wait to accept the next.
   */
  private readonly movingSignal = signal<string | null>(null);
  readonly moving = this.movingSignal.asReadonly();

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

  /** Null until the restaurant's settings have loaded, so the switch is not drawn guessing. */
  private readonly acceptingSignal = signal<boolean | null>(null);
  readonly accepting = this.acceptingSignal.asReadonly();

  /** Set while the switch is in flight, so it cannot be double-pressed into disagreement. */
  readonly pausing = signal(false);

  constructor() {
    // Reading revision() is what subscribes this effect to it, so the first run both registers
    // the dependency and loads the board. There is deliberately no separate initial load.
    effect(() => {
      this.stream.revision();
      void this.refresh();
    });

    void this.loadAcceptingOrders();

    const ticker = setInterval(() => this.nowSignal.set(Date.now()), TICK_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(ticker));
  }

  /**
   * Moves one order, then reloads the board.
   *
   * The reload is not strictly necessary — the server pushes the change to this restaurant's
   * group, which bumps the revision, which refetches — but waiting for a round trip through a
   * socket to redraw a button somebody just pressed is the difference between a screen that feels
   * immediate and one that feels broken. The push arriving a moment later is a harmless second
   * refresh.
   *
   * Returns whether it worked, so a caller knows whether to close a dialog.
   */
  async move(
    orderId: string,
    to: OrderStatus,
    reason: RejectionReason | null = null,
    note: string | null = null,
  ): Promise<boolean> {
    this.movingSignal.set(orderId);
    this.error.set(null);

    // Most often a 409: another tablet got there first, or the order moved on while this one was
    // being read. The message from the server says which, and it is worth showing verbatim.
    let refusal: string | null = null;

    try {
      await firstValueFrom(
        this.moveClient.changeStatus(orderId, {
          to,
          reason,
          note,
        }),
      );
    } catch (error) {
      refusal = describeError(error, 'Could not update the order.');
    }

    this.movingSignal.set(null);

    // Whether it worked or not. A refused move means this board was showing something stale,
    // which is exactly when it most needs re-reading.
    await this.refresh();

    // After the refresh, and that order is the point rather than an accident: a refresh that
    // succeeds clears the error signal, so setting the refusal first meant it vanished a
    // heartbeat later. The person saw a button do nothing and was told nothing.
    if (refusal !== null) {
      this.error.set(refusal);
    }

    return refusal === null;
  }

  private async loadAcceptingOrders(): Promise<void> {
    try {
      const settings = await firstValueFrom(this.settingsClient.get());
      this.acceptingSignal.set(settings.isAcceptingOrders);
    } catch {
      // Deliberately silent. Not knowing whether the restaurant is paused must not put an error
      // across a board full of orders that are perfectly readable; the switch simply stays hidden.
      this.acceptingSignal.set(null);
    }
  }

  /**
   * Pauses or resumes new orders. Separate from opening hours: the hours say when the kitchen
   * intends to be open, this says whether it can cope right now.
   */
  async setAcceptingOrders(accepting: boolean): Promise<void> {
    this.pausing.set(true);

    try {
      const settings = await firstValueFrom(
        this.settingsClient.setAcceptingOrders({ isAcceptingOrders: accepting }),
      );
      this.acceptingSignal.set(settings.isAcceptingOrders);
    } catch (error) {
      this.error.set(describeError(error, 'Could not change whether you are taking orders.'));
    } finally {
      this.pausing.set(false);
    }
  }

  /** Re-reads the queue. Called by the effect above, and by a person pressing refresh. */
  async refresh(): Promise<void> {
    try {
      // newestFirst false: a queue is worked from the order that has waited longest.
      const page = await firstValueFrom(
        this.client.queue([...LIVE_STATUSES], false, undefined, undefined, 1, PAGE_SIZE),
      );

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
