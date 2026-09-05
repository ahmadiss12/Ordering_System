import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { StaffMemberResponse, StaffRoleType } from 'api-client';
import { ConfirmData, ConfirmDialog } from '../common/confirm-dialog';
import { StaffStore } from './staff-store';
import { firstValueFrom } from 'rxjs';

/**
 * Who works here, and what they may do.
 *
 * <h4>Why this one is worded so carefully</h4>
 *
 * Every control on this card either hands somebody the restaurant's entire order book or takes it
 * away. There is no undo for the first and no self-service for the second, so each button says
 * what it does to a person rather than what it does to a row — "can see orders and the menu"
 * rather than "Staff".
 *
 * <h4>The last owner</h4>
 *
 * The buttons that would leave the restaurant without an owner are not drawn. The server refuses
 * them regardless; hiding them is about not offering somebody a door that is nailed shut.
 */
@Component({
  selector: 'app-staff',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTooltipModule,
  ],
  templateUrl: './staff.html',
  styleUrl: './staff.scss',
})
export class Staff {
  private readonly builder = inject(FormBuilder);
  private readonly dialog = inject(MatDialog);

  protected readonly store = inject(StaffStore);
  protected readonly Role = StaffRoleType;

  protected readonly form = this.builder.nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    phone: ['', Validators.maxLength(32)],
    staffRole: [StaffRoleType.Staff, Validators.required],
  });

  constructor() {
    void this.store.load();
  }

  protected roleLabel(role: StaffRoleType): string {
    return role === StaffRoleType.Owner ? 'Owner' : 'Staff';
  }

  protected roleDescription(role: StaffRoleType): string {
    return role === StaffRoleType.Owner
      ? 'Orders, the menu, and everything on this settings page'
      : 'Orders and the menu';
  }

  protected async invite(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { email, fullName, phone, staffRole } = this.form.getRawValue();

    const sent = await this.store.invite({
      email: email.trim(),
      fullName: fullName.trim(),
      phone: phone.trim() || null,
      staffRole,
    });

    if (sent) {
      this.form.reset({ staffRole: StaffRoleType.Staff });
    }
  }

  protected async setRole(member: StaffMemberResponse, role: StaffRoleType): Promise<void> {
    // Only the demotion needs asking about. Promoting somebody is undone by demoting them, but
    // demoting yourself shuts this page behind you — and that one is worth a sentence first.
    if (member.isYou && role !== StaffRoleType.Owner) {
      const sure = await this.confirm({
        title: 'Step back to staff?',
        message:
          'You will lose this settings page, your delivery zones, your opening hours and this ' +
          'staff list. Only another owner will be able to give them back to you.',
        confirm: 'Step back',
        destructive: true,
      });

      if (!sure) {
        return;
      }
    }

    await this.store.setRole(member, role);
  }

  protected async remove(member: StaffMemberResponse): Promise<void> {
    const sure = await this.confirm(
      member.isYou
        ? {
            title: 'Remove yourself from this restaurant?',
            message:
              'You will be signed out of it immediately and will not be able to get back in. ' +
              'Another owner would have to invite you again.',
            confirm: 'Remove myself',
            destructive: true,
          }
        : {
            title: `Remove ${member.fullName}?`,
            message:
              'They will be signed out straight away and will not see this restaurant again. ' +
              'Their own account and any orders they placed as a customer are untouched.',
            confirm: 'Remove',
            destructive: true,
          },
    );

    if (sure) {
      await this.store.remove(member);
    }
  }

  private async confirm(data: ConfirmData): Promise<boolean> {
    const answer = await firstValueFrom(
      this.dialog.open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, { data }).afterClosed(),
    );

    // Dismissing the dialog resolves undefined, which is not a yes.
    return answer === true;
  }
}
