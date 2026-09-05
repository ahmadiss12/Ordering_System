import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { PlatformRestaurantResponse } from 'api-client';
import { firstValueFrom } from 'rxjs';
import { ConfirmData, ConfirmDialog } from '../common/confirm-dialog';
import { PlatformStore } from './platform-store';

/** Matches the API's validator, so a mistyped rate is refused here with the same number. */
const MAX_COMMISSION_PERCENT = 50;

/**
 * The platform's own screen: what each restaurant is charged, and whether customers can find it.
 *
 * <h4>Two fields, both somebody else's livelihood</h4>
 *
 * A commission rate is what a restaurant earns and the listing switch is whether it exists as far
 * as customers are concerned. So this screen states the consequence of each press in words before
 * it happens, and says out loud what hiding a restaurant does <em>not</em> do — the orders already
 * cooking are unaffected, and its staff keep working them.
 *
 * <h4>The rate is typed, not nudged</h4>
 *
 * A field with a save button rather than a stepper. Commission moves rarely and deliberately, and
 * a control that changes it on an arrow-key press would be the wrong shape for that.
 */
@Component({
  selector: 'app-platform',
  providers: [PlatformStore],
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatTooltipModule,
  ],
  templateUrl: './platform.html',
  styleUrl: './platform.scss',
})
export class Platform {
  private readonly dialog = inject(MatDialog);

  protected readonly store = inject(PlatformStore);
  protected readonly maxCommission = MAX_COMMISSION_PERCENT;

  /** What has been typed into each rate box, until it is saved or abandoned. */
  private readonly typed = new Map<string, number>();

  constructor() {
    void this.store.load();
  }

  protected rate(row: PlatformRestaurantResponse): number {
    return this.typed.get(row.id) ?? row.commissionPercent;
  }

  protected onRate(row: PlatformRestaurantResponse, value: string): void {
    const parsed = Number(value);
    this.typed.set(row.id, Number.isFinite(parsed) ? parsed : row.commissionPercent);
  }

  protected isRateDirty(row: PlatformRestaurantResponse): boolean {
    return this.rate(row) !== row.commissionPercent;
  }

  protected isRateValid(row: PlatformRestaurantResponse): boolean {
    const rate = this.rate(row);
    return rate >= 0 && rate <= MAX_COMMISSION_PERCENT && Math.round(rate * 100) === rate * 100;
  }

  protected async saveRate(row: PlatformRestaurantResponse): Promise<void> {
    if (!this.isRateDirty(row) || !this.isRateValid(row)) {
      return;
    }

    const rate = this.rate(row);
    const sure = await this.confirm({
      title: `Charge ${row.name} ${rate}%?`,
      message:
        `They are on ${row.commissionPercent}% now. The new rate applies to orders placed from ` +
        'here on; everything already placed keeps the rate it was placed under.',
      confirm: 'Change the rate',
    });

    if (sure && (await this.store.setCommission(row, rate))) {
      // Dropped rather than set to the new value, so the box goes back to reading the server's
      // answer. If the number that came back differs from what was typed, that is worth seeing.
      this.typed.delete(row.id);
    }
  }

  protected discardRate(row: PlatformRestaurantResponse): void {
    this.typed.delete(row.id);
  }

  protected async toggleListing(row: PlatformRestaurantResponse): Promise<void> {
    if (row.isActive) {
      const waiting = row.liveOrderCount;
      const sure = await this.confirm({
        title: `Hide ${row.name} from customers?`,
        message:
          'Nobody will be able to find, quote or order from them until it is switched back on. ' +
          (waiting > 0
            ? `The ${waiting} order${waiting === 1 ? '' : 's'} already placed ${
                waiting === 1 ? 'is' : 'are'
              } not affected, and their kitchen can still work ${waiting === 1 ? 'it' : 'them'}.`
            : 'Orders already placed are not affected, and their kitchen keeps working.'),
        confirm: 'Hide them',
        destructive: true,
      });

      if (!sure) {
        return;
      }
    }

    await this.store.setListing(row, !row.isActive);
  }

  private async confirm(data: ConfirmData): Promise<boolean> {
    const answer = await firstValueFrom(
      this.dialog.open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, { data }).afterClosed(),
    );

    // Dismissing the dialog resolves undefined, which is not a yes.
    return answer === true;
  }
}
