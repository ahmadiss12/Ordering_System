import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { OptionResponse } from 'api-client';

export interface OptionDialogData {
  readonly groupName: string;
  readonly option?: OptionResponse;
  readonly sortOrder: number;
}

export interface OptionDialogResult {
  readonly name: string;
  readonly priceDeltaUsd: number;
  readonly maxQuantity: number;
  readonly isAvailable: boolean;
  readonly sortOrder: number;
}

/** A single choice inside a group: what it is called and what it adds to the price. */
@Component({
  selector: 'app-option-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSlideToggleModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.option ? 'Edit choice' : 'New choice' }}</h2>

    <mat-dialog-content>
      <p class="context">
        In <strong>{{ data.groupName }}</strong>
      </p>

      <mat-form-field appearance="outline" class="wide">
        <mat-label>Name</mat-label>
        <input
          matInput
          cdkFocusInitial
          maxlength="200"
          [ngModel]="name()"
          (ngModelChange)="name.set($event)"
        />
      </mat-form-field>

      <div class="row">
        <mat-form-field appearance="outline">
          <mat-label>Price change</mat-label>
          <span matTextPrefix>$&nbsp;</span>
          <input
            matInput
            type="text"
            inputmode="decimal"
            [ngModel]="price()"
            (ngModelChange)="price.set($event)"
          />
          <mat-hint>0 for no charge. A negative number takes money off.</mat-hint>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Max per order</mat-label>
          <input
            matInput
            type="number"
            min="1"
            max="20"
            [ngModel]="maxQuantity()"
            (ngModelChange)="maxQuantity.set($event)"
          />
          <mat-hint>1 unless it can be doubled up.</mat-hint>
        </mat-form-field>
      </div>

      <mat-slide-toggle [ngModel]="isAvailable()" (ngModelChange)="isAvailable.set($event)">
        Available
      </mat-slide-toggle>

      @if (error(); as message) {
        <p class="error" role="alert">{{ message }}</p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancel</button>
      <button matButton="filled" (click)="submit()">
        {{ data.option ? 'Save' : 'Add choice' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      min-width: min(30rem, 72vw);
    }

    .context {
      margin: 0 0 0.25rem;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }

    .wide {
      width: 100%;
    }

    .row {
      display: flex;
      gap: 1rem;

      mat-form-field:first-child {
        flex: 1 1 auto;
      }

      mat-form-field:last-child {
        width: 9rem;
      }
    }

    .error {
      margin: 0;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-error);
    }
  `,
})
export class OptionDialog {
  protected readonly data = inject<OptionDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<OptionDialog, OptionDialogResult>);

  protected readonly name = signal(this.data.option?.name ?? '');
  protected readonly price = signal<string | number>(this.data.option?.priceDeltaUsd ?? 0);
  protected readonly maxQuantity = signal<string | number>(this.data.option?.maxQuantity ?? 1);
  protected readonly isAvailable = signal(this.data.option?.isAvailable ?? true);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    const name = this.name().trim();
    if (name.length === 0) {
      this.error.set('Give the choice a name.');
      return;
    }

    // A delta of zero is "no pickles" and a negative one genuinely discounts the line, so only
    // the shape is checked here — the sign is not.
    const priceDeltaUsd = Number(this.price());
    if (
      !Number.isFinite(priceDeltaUsd) ||
      !/^-?\d+(\.\d{1,2})?$/.test(String(this.price()).trim())
    ) {
      this.error.set('Enter a price change like 0, 1.50 or -0.75.');
      return;
    }

    const maxQuantity = Number(this.maxQuantity());
    if (!Number.isInteger(maxQuantity) || maxQuantity < 1 || maxQuantity > 20) {
      // Mirrors the API's InclusiveBetween(1, 20).
      this.error.set('Max per order must be a whole number between 1 and 20.');
      return;
    }

    this.dialogRef.close({
      name,
      priceDeltaUsd,
      maxQuantity,
      isAvailable: this.isAvailable(),
      sortOrder: this.data.option?.sortOrder ?? this.data.sortOrder,
    });
  }
}
