import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { OrderStatus, RejectionReason } from 'api-client';
import { REJECTION_REASONS } from './order-wording';

export interface ReasonDialogData {
  readonly orderNumber: string;
  readonly customerName: string;
  readonly to: OrderStatus;
}

export interface ReasonDialogResult {
  readonly reason: RejectionReason;
  readonly note: string | null;
}

/**
 * Asks why, when the state machine will not accept the move without an answer.
 *
 * The reasons are radio buttons rather than a dropdown: six options is few enough to read at once,
 * and a kitchen tablet is being pressed with a thumb, where a dropdown is two taps and a scroll.
 *
 * The list is fixed because the rejection-rate report groups by it, and a report cannot group by a
 * sentence somebody typed. The note is where the sentence goes, and it is always optional — asking
 * a busy kitchen to write an essay is how you get "asdf".
 */
@Component({
  selector: 'app-reason-dialog',
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ title }}</h2>

    <mat-dialog-content>
      <p class="who">{{ data.orderNumber }} &middot; {{ data.customerName }}</p>

      <mat-radio-group [ngModel]="reason()" (ngModelChange)="reason.set($event)">
        @for (option of reasons; track option.value) {
          <mat-radio-button [value]="option.value">{{ option.label }}</mat-radio-button>
        }
      </mat-radio-group>

      <mat-form-field appearance="outline">
        <mat-label>Anything to add? (optional)</mat-label>
        <input matInput [ngModel]="note()" (ngModelChange)="note.set($event)" maxlength="500" />
        <mat-hint>The customer sees this.</mat-hint>
      </mat-form-field>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close cdkFocusInitial>Keep the order</button>
      <button matButton="filled" class="destructive" [mat-dialog-close]="result()">
        {{ confirm }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding-top: 0.5rem;
    }

    .who {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }

    mat-radio-group {
      display: flex;
      flex-direction: column;
    }

    .destructive {
      --mat-button-filled-container-color: var(--mat-sys-error);
      --mat-button-filled-label-text-color: var(--mat-sys-on-error);
    }
  `,
})
export class ReasonDialog {
  protected readonly data = inject<ReasonDialogData>(MAT_DIALOG_DATA);
  protected readonly reasons = REJECTION_REASONS;

  // Pre-selected rather than blank. Out of stock is far and away the commonest answer, and a
  // required field with nothing chosen is one more press between a busy kitchen and the truth.
  protected readonly reason = signal(RejectionReason.OutOfStock);
  protected readonly note = signal('');

  protected get title(): string {
    return this.data.to === OrderStatus.Rejected ? 'Refuse this order?' : "Can't complete it?";
  }

  protected get confirm(): string {
    return this.data.to === OrderStatus.Rejected ? 'Refuse order' : 'Cancel order';
  }

  protected result(): ReasonDialogResult {
    const note = this.note().trim();
    return { reason: this.reason(), note: note.length > 0 ? note : null };
  }
}
