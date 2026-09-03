import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, effect, inject, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { FulfillmentType, OrderStatus } from 'api-client';
import { OrderStream } from 'realtime';
import { firstValueFrom } from 'rxjs';
import { Chime } from './chime';
import { OrderAction, actionsFor } from './order-actions';
import { OrderDetailDialog, OrderDetailDialogData } from './order-detail-dialog';
import { QueuedOrder } from './queue-model';
import { QueueStore } from './queue-store';
import { ReasonDialog, ReasonDialogData, ReasonDialogResult } from './reason-dialog';

/**
 * The screen staff live in during service.
 *
 * Built for a tablet propped up somewhere greasy and read at arm's length by somebody with their
 * hands full, which is why the type is large, the columns are wide, and the only colour on the
 * board means something. Nothing here is hover-only and nothing is smaller than a thumb.
 *
 * It never asks whether the live channel is up. {@link QueueStore} refreshes on one signal that
 * covers a pushed message, a reconnection and the poll behind it, so the board simply draws what
 * it has — and says, quietly, whether the connection is live, because somebody deciding whether
 * to trust the screen deserves to know.
 *
 * The buttons come from each order's own `availableTransitions`, which the API fills from the
 * transition table. Nothing here decides what may follow what; a screen that worked it out itself
 * would be a second copy of the rule, and the first copy is the one the server enforces.
 */
@Component({
  selector: 'app-queue',
  providers: [QueueStore],
  imports: [
    CurrencyPipe,
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatProgressBarModule,
    MatTooltipModule,
  ],
  templateUrl: './queue.html',
  styleUrl: './queue.scss',
})
export class Queue {
  private readonly dialog = inject(MatDialog);
  private readonly stream = inject(OrderStream);

  protected readonly store = inject(QueueStore);
  protected readonly chime = inject(Chime);

  protected readonly FulfillmentType = FulfillmentType;

  constructor() {
    // A brand-new order is the one thing worth interrupting somebody for. Everything else on this
    // board can wait until they next glance at it; an unanswered order cannot.
    effect(() => {
      const change = this.stream.lastChange();

      // previousStatus is null only for an order that came from nowhere — a placement. A move
      // between statuses is a kitchen's own press coming back to it, which would be an odd thing
      // to be told about.
      if (change?.previousStatus === null) {
        untracked(() => this.chime.play());
      }
    });
  }

  /** "3 min", or "just now" for the first minute, which is the honest thing to call it. */
  protected waited(order: QueuedOrder): string {
    return order.waitingMinutes < 1 ? 'just now' : `${order.waitingMinutes} min ago`;
  }

  /**
   * What the promise looks like from here: minutes left, or how far past it already is.
   * Null for an order at the pass, where the countdown has stopped meaning anything.
   */
  protected promise(order: QueuedOrder): string | null {
    const left = order.minutesToPromise;

    if (left === null) {
      return null;
    }

    if (left < 0) {
      return `${Math.abs(left)} min over`;
    }

    return left === 0 ? 'due now' : `${left} min left`;
  }

  protected actions(order: QueuedOrder): readonly OrderAction[] {
    return actionsFor(order.order.availableTransitions, order.order.fulfillment);
  }

  /**
   * Presses a button.
   *
   * Also the moment the browser will let the chime start, because it is a real user gesture —
   * which is why unlocking happens here rather than somewhere that looks tidier.
   */
  protected async press(row: QueuedOrder, action: OrderAction): Promise<void> {
    void this.chime.unlock();

    if (!action.needsReason) {
      await this.store.move(row.order.id, action.to);
      return;
    }

    const answer = await firstValueFrom(
      this.dialog
        .open<ReasonDialog, ReasonDialogData, ReasonDialogResult>(ReasonDialog, {
          width: '26rem',
          data: {
            orderNumber: row.order.orderNumber,
            customerName: row.order.customerName,
            to: action.to,
          },
        })
        .afterClosed(),
    );

    if (answer) {
      await this.store.move(row.order.id, action.to, answer.reason, answer.note);
    }
  }

  /** Opens the receipt. Read-only: the buttons that move the order are on the card behind it. */
  protected openDetail(row: QueuedOrder): void {
    this.dialog.open<OrderDetailDialog, OrderDetailDialogData>(OrderDetailDialog, {
      width: '32rem',
      maxHeight: '85vh',
      data: { orderId: row.order.id },
    });
  }

  protected toggleSound(): void {
    this.chime.setEnabled(!this.chime.enabled());
  }

  protected readonly OrderStatus = OrderStatus;
}
