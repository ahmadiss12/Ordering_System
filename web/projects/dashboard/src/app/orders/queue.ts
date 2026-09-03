import { DatePipe, CurrencyPipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { FulfillmentType } from 'api-client';
import { QueuedOrder } from './queue-model';
import { QueueStore } from './queue-store';

/**
 * The screen staff live in during service.
 *
 * Built for a tablet propped up somewhere greasy and read at arm's length by somebody with their
 * hands full, which is why the type is large, the columns are wide, and the only colour on the
 * board means something. Nothing here is hover-only and nothing is smaller than a thumb.
 *
 * It never asks whether the live channel is up. {@link QueueStore} refreshes on one signal that
 * covers a pushed message, a reconnection and the poll behind it, so the board simply draws what
 * it has — and says, quietly, whether the connection is live, because a member of staff deciding
 * whether to trust the screen deserves to know.
 */
@Component({
  selector: 'app-queue',
  providers: [QueueStore],
  imports: [
    CurrencyPipe,
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    MatTooltipModule,
  ],
  templateUrl: './queue.html',
  styleUrl: './queue.scss',
})
export class Queue {
  protected readonly store = inject(QueueStore);

  protected readonly FulfillmentType = FulfillmentType;

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
}
