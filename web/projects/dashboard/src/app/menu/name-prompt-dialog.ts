import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface NamePromptData {
  readonly title: string;
  readonly label: string;
  readonly value?: string;
  readonly confirm: string;
}

/**
 * Asks for a single name. Used for adding a section and for renaming one.
 *
 * A dialog rather than an inline field because the same shape is needed from two places, and
 * because a form that appears in the row you are reading pushes the rest of the list around.
 */
@Component({
  selector: 'app-name-prompt-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>

    <mat-dialog-content>
      <mat-form-field appearance="outline" class="field">
        <mat-label>{{ data.label }}</mat-label>
        <input
          matInput
          cdkFocusInitial
          maxlength="200"
          [ngModel]="name()"
          (ngModelChange)="name.set($event)"
          (keyup.enter)="submit()"
        />
      </mat-form-field>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancel</button>
      <button matButton="filled" [disabled]="!isValid()" (click)="submit()">
        {{ data.confirm }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .field {
      width: 100%;
      min-width: 20rem;
    }
  `,
})
export class NamePromptDialog {
  protected readonly data = inject<NamePromptData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<NamePromptDialog, string>);

  protected readonly name = signal(this.data.value ?? '');

  protected isValid(): boolean {
    return this.name().trim().length > 0;
  }

  protected submit(): void {
    if (this.isValid()) {
      this.dialogRef.close(this.name().trim());
    }
  }
}
