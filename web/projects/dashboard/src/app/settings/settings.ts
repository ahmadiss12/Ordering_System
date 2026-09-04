import { Component, effect, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { SessionStore, Roles } from 'auth';
import { Hours } from './hours';
import { HoursStore } from './hours-store';
import { SettingsStore } from './settings-store';

/** Matches the API's validator, so a mistyped figure is refused here with the same numbers. */
const MAX_PREP_MINUTES = 120;
const MAX_MIN_ORDER_USD = 500;

/**
 * The restaurant's own settings — the screen this application has been promising since Phase 2.
 *
 * <h4>Two audiences on one page</h4>
 *
 * An owner sees a form. A staff member sees the same facts and one switch, because pausing orders
 * is the thing a cook needs mid-service and everything else on this page is a decision somebody
 * makes sitting down. The API enforces exactly that split; this screen only draws it, so a staff
 * member never meets a form that would be refused on submit.
 *
 * <h4>What is shown but not editable</h4>
 *
 * Commission, and whether the platform has the restaurant switched on. A restaurant is entitled to
 * know both — the first is what it is being charged — and neither is theirs to change.
 */
@Component({
  selector: 'app-settings',
  providers: [SettingsStore, HoursStore],
  imports: [
    Hours,
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSlideToggleModule,
  ],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings {
  private readonly session = inject(SessionStore);
  private readonly builder = inject(FormBuilder);

  protected readonly store = inject(SettingsStore);

  protected readonly isOwner = this.session.hasAnyRole(Roles.RestaurantOwner);
  protected readonly maxPrepMinutes = MAX_PREP_MINUTES;
  protected readonly maxMinOrderUsd = MAX_MIN_ORDER_USD;

  protected readonly form = this.builder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', Validators.maxLength(1000)],
    phone: ['', [Validators.required, Validators.maxLength(32)]],
    defaultPrepMinutes: [
      20,
      [Validators.required, Validators.min(1), Validators.max(MAX_PREP_MINUTES)],
    ],
    minOrderUsd: [0, [Validators.required, Validators.min(0), Validators.max(MAX_MIN_ORDER_USD)]],
  });

  constructor() {
    void this.store.load();

    // Fills the form from whatever the server last said, including after a save — so the boxes
    // show the trimmed, stored values rather than what was typed.
    effect(() => {
      const settings = this.store.settings();

      if (settings) {
        this.form.reset(
          {
            name: settings.name,
            description: settings.description ?? '',
            phone: settings.phone,
            defaultPrepMinutes: settings.defaultPrepMinutes,
            minOrderUsd: settings.minOrderUsd,
          },
          { emitEvent: false },
        );

        // Staff see the values; they do not get to change them. The API would refuse anyway.
        if (!this.isOwner) {
          this.form.disable({ emitEvent: false });
        }
      }
    });
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    await this.store.save(this.form.getRawValue());
  }

  protected async togglePause(accepting: boolean): Promise<void> {
    await this.store.setAcceptingOrders(accepting);
  }
}
