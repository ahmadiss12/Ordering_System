import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
    ReactiveFormsModule,
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
  private readonly builder = inject(FormBuilder);

  protected readonly store = inject(PlatformStore);
  protected readonly maxCommission = MAX_COMMISSION_PERCENT;

  /** Whether the "take a restaurant on" form is open. Shut by default: it is a rare act. */
  protected readonly adding = signal(false);

  protected readonly form = this.builder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    // Optional, and derived from the name when left alone — which is what somebody typing a
    // restaurant's name usually wants, rather than being made to invent a URL.
    slug: ['', [Validators.maxLength(120), Validators.pattern(/^[a-z0-9]+(-[a-z0-9]+)*$/)]],
    phone: ['', [Validators.required, Validators.maxLength(32)]],
    commissionPercent: [
      15,
      [Validators.required, Validators.min(0), Validators.max(MAX_COMMISSION_PERCENT)],
    ],
    ownerEmail: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    ownerFullName: ['', [Validators.required, Validators.maxLength(200)]],
    ownerPhone: ['', Validators.maxLength(32)],
  });

  /** What the link will be if nothing is typed into the slug box, shown live under the name. */
  protected readonly derivedSlug = signal('');

  /** What has been typed into each rate box, until it is saved or abandoned. */
  private readonly typed = new Map<string, number>();

  constructor() {
    void this.store.load();

    // Shown rather than explained: somebody typing "Café Beirut & Sons" can see it will live at
    // /cafe-beirut-sons before they commit to it, and type their own if they would rather not.
    effect(() => {
      const name = this.form.controls.name.value;
      this.derivedSlug.set(slugFrom(name));
    });

    this.form.controls.name.valueChanges.subscribe((name) =>
      this.derivedSlug.set(slugFrom(name ?? '')),
    );
  }

  protected async takeOn(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();

    const created = await this.store.create({
      name: value.name.trim(),
      slug: value.slug.trim() || null,
      phone: value.phone.trim(),
      commissionPercent: value.commissionPercent,
      ownerEmail: value.ownerEmail.trim(),
      ownerFullName: value.ownerFullName.trim(),
      ownerPhone: value.ownerPhone.trim() || null,
    });

    if (created) {
      this.form.reset({ commissionPercent: 15 });
      this.adding.set(false);
    }
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

/**
 * The same rules the server applies, so the preview under the name box is what will actually be
 * saved rather than an optimistic guess.
 *
 * Kept deliberately small: it only has to agree with the server for names an admin is likely to
 * type, and the server is the one that decides — a name it cannot make a link from is refused
 * there with a message asking for one.
 */
function slugFrom(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}
