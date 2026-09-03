import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import {
  FulfillmentType,
  MyOrdersClient,
  OrderDetailResponse,
  OrderStatus,
  PaymentMethod,
  PaymentStatus,
  describeError,
} from 'api-client';
import { firstValueFrom } from 'rxjs';
import { eventLabel, reasonLabel, statusLabel, statusTone } from './order-wording';

export interface OrderDetailDialogData {
  readonly orderId: string;
}

/**
 * One order, in full: what was ordered, what it cost, where it went, and everything that happened
 * to it.
 *
 * <h4>Why it loads its own order</h4>
 *
 * It is given an id, not a row. A caller handing over the summary it already has would be handing
 * over a copy that is as old as its last refresh, and this is the screen somebody opens precisely
 * because they want to know exactly what happened.
 *
 * <h4>Why it is read-only</h4>
 *
 * The buttons live on the queue card behind it. Repeating them here would be a second copy of the
 * press logic for a screen whose whole job is to explain rather than to act — and in the history,
 * where this is opened most, every order is finished and offers no moves at all.
 */
@Component({
  selector: 'app-order-detail-dialog',
  imports: [
    CurrencyPipe,
    DatePipe,
    DecimalPipe,
    MatButtonModule,
    MatDialogModule,
    MatDividerModule,
    MatIconModule,
    MatProgressBarModule,
  ],
  templateUrl: './order-detail-dialog.html',
  styleUrl: './order-detail-dialog.scss',
})
export class OrderDetailDialog {
  private readonly client = inject(MyOrdersClient);
  private readonly data = inject<OrderDetailDialogData>(MAT_DIALOG_DATA);

  protected readonly order = signal<OrderDetailResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly FulfillmentType = FulfillmentType;
  protected readonly PaymentMethod = PaymentMethod;
  protected readonly PaymentStatus = PaymentStatus;

  protected readonly statusLabel = statusLabel;
  protected readonly eventLabel = eventLabel;
  protected readonly statusTone = statusTone;

  /**
   * Why the restaurant dropped it, when it did. Null on every other order, which is what keeps
   * the panel off the screen rather than showing an empty one.
   */
  protected readonly refusal = computed(() => {
    const order = this.order();

    // == null, not === undefined. The generated client types every optional field as
    // `| null` because that is what arrives: the response is JSON.parse'd straight through,
    // and JSON has no undefined. A strict check against undefined here showed the refusal
    // panel on every order that had never been refused.
    if (!order || order.rejectionReason == null) {
      return null;
    }

    return {
      heading: order.status === OrderStatus.Rejected ? 'Refused' : 'Could not be completed',
      reason: reasonLabel(order.rejectionReason),
      note: order.rejectionNote ?? null,
    };
  });

  /** The address as it was recorded, on the lines a driver would read it in. */
  protected readonly addressLines = computed(() => {
    const address = this.order()?.deliveryAddress;

    if (!address) {
      return [];
    }

    // Filtered rather than joined with separators, so a missing floor does not leave a stray
    // comma on a delivery slip.
    return [address.line1, address.building, address.floor, address.landmark, address.zoneName]
      .map((part) => part?.trim())
      .filter((part): part is string => !!part);
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.order.set(await firstValueFrom(this.client.byId(this.data.orderId)));
    } catch (error) {
      this.error.set(describeError(error, 'Could not load the order.'));
    } finally {
      this.loading.set(false);
    }
  }
}
