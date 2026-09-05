import { Injectable, computed, inject, signal } from '@angular/core';
import { RestaurantReportResponse, RestaurantReportsClient, describeError } from 'api-client';
import { firstValueFrom } from 'rxjs';

/** Matches the API's validator, so a range this screen offers is one the server will accept. */
export const MAX_RANGE_DAYS = 366;

export interface RangePreset {
  readonly key: string;
  readonly label: string;
  readonly days: number;
}

/** The ranges somebody actually asks for, shortest first. */
export const RANGE_PRESETS: readonly RangePreset[] = [
  { key: 'week', label: 'Last 7 days', days: 7 },
  { key: 'month', label: 'Last 30 days', days: 30 },
  { key: 'quarter', label: 'Last 90 days', days: 90 },
];

/**
 * What the restaurant did, over a range of its own calendar days.
 *
 * <h4>Dates are strings here, deliberately</h4>
 *
 * Every date in and out of this store is a plain `yyyy-MM-dd`, never a `Date`. A calendar day has
 * no time and no timezone; a `Date` is an instant, and turning one into the other is where an
 * owner in Beirut asks for the 5th and gets the 4th. The API types these as strings for the same
 * reason.
 */
@Injectable()
export class ReportStore {
  private readonly client = inject(RestaurantReportsClient);

  private readonly reportSignal = signal<RestaurantReportResponse | null>(null);

  readonly report = this.reportSignal.asReadonly();
  readonly loading = signal(true);
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);

  readonly from = signal(startOf(RANGE_PRESETS[1].days));
  readonly to = signal(today());

  /** Which preset the current range happens to match, so the chips can show one as chosen. */
  readonly preset = computed(() => {
    const from = this.from();
    const to = this.to();

    return to === today()
      ? (RANGE_PRESETS.find((p) => startOf(p.days) === from)?.key ?? null)
      : null;
  });

  readonly rangeIsValid = computed(() => {
    const days = dayNumber(this.to()) - dayNumber(this.from());
    return days >= 0 && days < MAX_RANGE_DAYS;
  });

  async load(): Promise<void> {
    if (!this.rangeIsValid()) {
      return;
    }

    this.loading.set(true);

    try {
      this.reportSignal.set(await firstValueFrom(this.client.summary(this.from(), this.to())));
      this.error.set(null);
      this.loaded.set(true);
    } catch (error) {
      this.error.set(describeError(error, 'Could not load your report.'));
    } finally {
      this.loading.set(false);
    }
  }

  async choose(preset: RangePreset): Promise<void> {
    this.from.set(startOf(preset.days));
    this.to.set(today());
    await this.load();
  }

  async setFrom(value: string): Promise<void> {
    this.from.set(value);
    await this.load();
  }

  async setTo(value: string): Promise<void> {
    this.to.set(value);
    await this.load();
  }
}

/**
 * Today as the browser's own calendar says it, formatted by hand.
 *
 * `toISOString()` is deliberately avoided: it converts to UTC first, so anywhere east of
 * Greenwich it names yesterday for the first hours of the day — which for a Beirut restaurant is
 * every evening. The server has the final word on what "today" means anyway; this only needs to
 * put a sensible default in the box.
 */
function today(): string {
  const now = new Date();
  const month = `${now.getMonth() + 1}`.padStart(2, '0');
  const day = `${now.getDate()}`.padStart(2, '0');

  return `${now.getFullYear()}-${month}-${day}`;
}

/** The first day of a range of `days` ending today, both ends included. */
function startOf(days: number): string {
  const start = new Date();
  start.setDate(start.getDate() - (days - 1));

  const month = `${start.getMonth() + 1}`.padStart(2, '0');
  const day = `${start.getDate()}`.padStart(2, '0');

  return `${start.getFullYear()}-${month}-${day}`;
}

/** Days since the epoch, for comparing two `yyyy-MM-dd` strings without parsing timezones. */
function dayNumber(date: string): number {
  return Date.UTC(+date.slice(0, 4), +date.slice(5, 7) - 1, +date.slice(8, 10)) / 86_400_000;
}
