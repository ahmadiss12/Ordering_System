import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { DayOfWeek } from 'api-client';
import { firstValueFrom } from 'rxjs';
import { ConfirmData, ConfirmDialog } from '../common/confirm-dialog';
import { DraftWindow, HoursStore, isOvernight } from './hours-store';

/**
 * When the restaurant is open.
 *
 * <h4>The two things this screen has to make sayable</h4>
 *
 * A day with more than one window — lunch, a gap, dinner — and a window that runs past midnight.
 * The second is the one people get wrong: a close time earlier than an open time is not a mistake
 * here, it is how "noon until two in the morning" is written, so the screen says so in words the
 * moment somebody types it rather than leaving them to wonder.
 *
 * <h4>Why saving is a deliberate act</h4>
 *
 * The week is sent whole and refused if two windows clash, so a draft is allowed to be briefly
 * wrong on its way to being right. Saving each change as it happened would refuse edits that were
 * halfway finished.
 */
@Component({
  selector: 'app-hours',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatProgressBarModule,
    MatTooltipModule,
  ],
  templateUrl: './hours.html',
  styleUrl: './hours.scss',
})
export class Hours {
  private readonly dialog = inject(MatDialog);

  protected readonly store = inject(HoursStore);
  protected readonly isOvernight = isOvernight;

  constructor() {
    void this.store.load();
  }

  protected onTime(day: DayOfWeek, index: number, field: 'open' | 'close', event: Event): void {
    this.store.setTime(day, index, field, (event.target as HTMLInputElement).value);
  }

  /** "closes at 02:00 the next day", so nobody has to work out what the earlier time means. */
  protected overnightNote(window: DraftWindow): string | null {
    return isOvernight(window) ? `closes ${window.close} the next day` : null;
  }

  protected async save(): Promise<void> {
    if (!this.store.wouldCloseIndefinitely()) {
      await this.store.save();
      return;
    }

    // An empty week is legitimate — a kitchen closing for August — and is also what this screen
    // looks like halfway through an edit. The API refuses it without a confirmation for exactly
    // that reason, so this is the confirmation.
    const confirmed = await firstValueFrom(
      this.dialog
        .open<ConfirmDialog, ConfirmData, true>(ConfirmDialog, {
          data: {
            title: 'Close indefinitely?',
            message:
              'You have no opening hours left. Customers will not be able to order from you at ' +
              'all until you add some back.',
            confirm: 'Close indefinitely',
            destructive: true,
          },
        })
        .afterClosed(),
    );

    if (confirmed) {
      await this.store.save(true);
    }
  }
}
