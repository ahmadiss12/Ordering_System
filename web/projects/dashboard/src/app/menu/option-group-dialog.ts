import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { OptionGroupResponse } from 'api-client';
import { RulePicker } from './rule-picker';
import { SelectionRule, ruleFrom, ruleIsValid } from './selection-rule';

export interface OptionGroupDialogData {
  readonly group?: OptionGroupResponse;
}

export interface OptionGroupDialogResult {
  readonly name: string;
  readonly rule: SelectionRule;
}

/**
 * A group of choices — "Sauces", "Size" — shared across the menu rather than owned by one dish.
 *
 * That sharing is the reason the per-item override exists, and the reason this dialog says so:
 * renaming a group here renames it everywhere it is used.
 */
@Component({
  selector: 'app-option-group-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    RulePicker,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.group ? 'Edit option group' : 'New option group' }}</h2>

    <mat-dialog-content>
      <mat-form-field appearance="outline" class="name">
        <mat-label>Group name</mat-label>
        <input
          matInput
          cdkFocusInitial
          maxlength="200"
          [ngModel]="name()"
          (ngModelChange)="name.set($event)"
        />
        <mat-hint>What customers see above the choices, like "Sauces" or "Size".</mat-hint>
      </mat-form-field>

      <app-rule-picker [(rule)]="rule" />

      @if (data.group) {
        <p class="shared-note">
          This group can be used by more than one item. Changing it here changes it everywhere —
          unless an item overrides it for itself.
        </p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancel</button>
      <button matButton="filled" [disabled]="!canSave()" (click)="submit()">
        {{ data.group ? 'Save' : 'Create group' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      min-width: min(32rem, 72vw);
    }

    .name {
      width: 100%;
    }

    .shared-note {
      margin: 0;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class OptionGroupDialog {
  protected readonly data = inject<OptionGroupDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<OptionGroupDialog, OptionGroupDialogResult>);

  protected readonly name = signal(this.data.group?.name ?? '');
  protected readonly rule = signal<SelectionRule>(
    this.data.group ? ruleFrom(this.data.group) : { minSelect: 0, maxSelect: null },
  );

  protected canSave(): boolean {
    return this.name().trim().length > 0 && ruleIsValid(this.rule());
  }

  protected submit(): void {
    if (this.canSave()) {
      this.dialogRef.close({ name: this.name().trim(), rule: this.rule() });
    }
  }
}
