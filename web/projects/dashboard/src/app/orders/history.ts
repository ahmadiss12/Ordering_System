import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { FulfillmentType, OrderSummaryResponse } from 'api-client';
import { HISTORY_FILTERS, HistoryStore } from './history-store';
import { OrderDetailDialog, OrderDetailDialogData } from './order-detail-dialog';
import { reasonLabel, statusLabel, statusTone } from './order-wording';

/**
 * What already happened: last night's orders, and why the refused ones were refused.
 *
 * The other half of the queue, and deliberately a different kind of screen. The queue is glanced
 * at with both hands full and redraws itself; this is read sitting down, so it holds still, pages
 * rather than scrolls forever, and puts the refusal reason on the row — somebody counting how
 * often the fryer went down should not have to open twenty orders to find out.
 */
@Component({
  selector: 'app-history',
  providers: [HistoryStore],
  imports: [
    CurrencyPipe,
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatDialogModule,
    MatIconModule,
    MatProgressBarModule,
  ],
  templateUrl: './history.html',
  styleUrl: './history.scss',
})
export class History {
  private readonly dialog = inject(MatDialog);

  protected readonly store = inject(HistoryStore);
  protected readonly filters = HISTORY_FILTERS;

  protected readonly FulfillmentType = FulfillmentType;
  protected readonly statusLabel = statusLabel;
  protected readonly statusTone = statusTone;
  protected readonly reasonLabel = reasonLabel;

  protected open(order: OrderSummaryResponse): void {
    this.dialog.open<OrderDetailDialog, OrderDetailDialogData>(OrderDetailDialog, {
      width: '32rem',
      maxHeight: '85vh',
      data: { orderId: order.id },
    });
  }
}
