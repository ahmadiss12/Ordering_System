import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ZoneRow, ZonesStore } from './zones-store';

/** Matches the API's validator, so a mistyped figure is refused here with the same numbers. */
const MAX_FEE_USD = 50;
const MAX_MINUTES = 180;

/**
 * Where the restaurant delivers, and what it charges to get there.
 *
 * <h4>Why each row saves itself</h4>
 *
 * Zones are independent — serving Hamra says nothing about Achrafieh — so a row that fails to save
 * is a row that failed, not a mystery somewhere in a grid of ten. The API is per-zone for the same
 * reason, and this screen matches it rather than inventing a bulk save on top.
 */
@Component({
  selector: 'app-zones',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    MatSlideToggleModule,
    MatTooltipModule,
  ],
  templateUrl: './zones.html',
  styleUrl: './zones.scss',
})
export class Zones {
  protected readonly store = inject(ZonesStore);

  protected readonly maxFee = MAX_FEE_USD;
  protected readonly maxMinutes = MAX_MINUTES;

  constructor() {
    void this.store.load();
  }

  protected onNumber(zoneId: string, field: 'feeUsd' | 'minutes', event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);

    // An empty box parses to NaN, which would travel to the API as null and be refused for a
    // reason that has nothing to do with what somebody typed.
    if (!Number.isNaN(value)) {
      this.store.update(zoneId, { [field]: value });
    }
  }

  protected onServed(zoneId: string, isServed: boolean): void {
    this.store.update(zoneId, { isServed });
  }

  protected served(): number {
    return this.store.rows().filter((r) => r.isServed).length;
  }

  protected isDirty(row: ZoneRow): boolean {
    return this.store.isDirty(row);
  }
}
