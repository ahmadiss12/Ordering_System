import { Injectable, inject, signal } from '@angular/core';
import { RestaurantZoneResponse, RestaurantZonesClient, describeError } from 'api-client';
import { firstValueFrom } from 'rxjs';

/** What a zone that has never been configured starts at when somebody switches it on. */
const DEFAULT_FEE_USD = 2;
const DEFAULT_MINUTES = 20;

/** One zone's row, as the screen holds it while somebody edits. */
export interface ZoneRow {
  readonly zoneId: string;
  readonly zoneName: string;
  isServed: boolean;
  feeUsd: number;
  minutes: number;
  /** What the server last confirmed, so a row knows whether it has changed. */
  readonly saved: { isServed: boolean; feeUsd: number; minutes: number };
}

/**
 * Where the restaurant delivers.
 *
 * <h4>A row at a time, unlike the hours</h4>
 *
 * Zones are independent: serving Hamra says nothing about Achrafieh and nothing can conflict, so
 * each row saves on its own. That means a failure is scoped to the row that failed rather than
 * leaving somebody guessing which of ten changes did not take — which is what a single save button
 * over a grid of independent things buys you.
 */
@Injectable()
export class ZonesStore {
  private readonly client = inject(RestaurantZonesClient);

  private readonly rowsSignal = signal<ZoneRow[]>([]);

  readonly rows = this.rowsSignal.asReadonly();
  readonly loading = signal(true);
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);

  /** The zone currently being saved, so only its own controls are disabled. */
  readonly saving = signal<string | null>(null);

  async load(): Promise<void> {
    this.loading.set(true);

    try {
      const zones = await firstValueFrom(this.client.list());

      this.rowsSignal.set(zones.map(toRow));
      this.error.set(null);
      this.loaded.set(true);
    } catch (error) {
      this.error.set(describeError(error, 'Could not load your delivery zones.'));
    } finally {
      this.loading.set(false);
    }
  }

  isDirty(row: ZoneRow): boolean {
    return (
      row.isServed !== row.saved.isServed ||
      row.feeUsd !== row.saved.feeUsd ||
      row.minutes !== row.saved.minutes
    );
  }

  update(zoneId: string, changes: Partial<Pick<ZoneRow, 'isServed' | 'feeUsd' | 'minutes'>>): void {
    this.rowsSignal.update((rows) =>
      rows.map((row) => (row.zoneId === zoneId ? { ...row, ...changes } : row)),
    );
  }

  async save(zoneId: string): Promise<boolean> {
    const row = this.rowsSignal().find((r) => r.zoneId === zoneId);
    if (!row) {
      return false;
    }

    this.saving.set(zoneId);
    this.error.set(null);

    try {
      const saved = await firstValueFrom(
        this.client.set(zoneId, {
          isServed: row.isServed,
          deliveryFeeUsd: row.feeUsd,
          estimatedMinutes: row.minutes,
        }),
      );

      this.rowsSignal.update((rows) => rows.map((r) => (r.zoneId === zoneId ? toRow(saved) : r)));
      return true;
    } catch (error) {
      // Named, because a grid of ten rows with one failure needs to say which one.
      this.error.set(describeError(error, `Could not save ${row.zoneName}.`));
      return false;
    } finally {
      this.saving.set(null);
    }
  }

  /** Puts one row back to what the server last confirmed. */
  discard(zoneId: string): void {
    this.rowsSignal.update((rows) =>
      rows.map((row) => (row.zoneId === zoneId ? { ...row, ...row.saved } : row)),
    );
  }
}

function toRow(zone: RestaurantZoneResponse): ZoneRow {
  // A zone never configured starts at something plausible rather than at zero: free delivery in
  // no time at all is a promise nobody meant to make by pressing one switch.
  const feeUsd = zone.deliveryFeeUsd ?? DEFAULT_FEE_USD;
  const minutes = zone.estimatedMinutes ?? DEFAULT_MINUTES;

  return {
    zoneId: zone.zoneId,
    zoneName: zone.zoneName,
    isServed: zone.isServed,
    feeUsd,
    minutes,
    saved: { isServed: zone.isServed, feeUsd, minutes },
  };
}
