import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RejectionBreakdown } from 'api-client';
import { reasonLabel } from '../orders/order-wording';
import {
  Bar,
  CHART,
  dayLabel,
  labelEvery,
  ordersAxis,
  revenueAxis,
  shortDayLabel,
  startsMonth,
} from './chart-model';
import { MAX_RANGE_DAYS, RANGE_PRESETS, RangePreset, ReportStore } from './report-store';

/**
 * What the restaurant did, and what it turned away.
 *
 * <h4>Two charts, one day axis</h4>
 *
 * Orders and revenue are measures of different size, so they get a plot each rather than two
 * y-axes on one — a shared axis between them would invent whatever relationship the scales
 * happened to imply. They line up column for column, which is the comparison worth having.
 *
 * <h4>The headline figures are not charts</h4>
 *
 * Revenue, orders, the average and the rejection rate are each one number. A number is better
 * read as a number than as a bar of length one.
 */
@Component({
  selector: 'app-reports',
  providers: [ReportStore],
  imports: [
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  templateUrl: './reports.html',
  styleUrl: './reports.scss',
})
export class Reports {
  protected readonly store = inject(ReportStore);

  protected readonly presets = RANGE_PRESETS;
  protected readonly chart = CHART;
  protected readonly maxRangeDays = MAX_RANGE_DAYS;

  /** Which bar the pointer is on, shared by both charts so a day highlights in each at once. */
  protected readonly hovered = signal<string | null>(null);

  protected readonly days = computed(() => this.store.report()?.days ?? []);
  protected readonly orders = computed(() => ordersAxis(this.days()));
  protected readonly revenue = computed(() => revenueAxis(this.days()));

  /** Every nth day gets a label, so thirty of them do not overlap into a smear. */
  protected readonly labelStep = computed(() => labelEvery(this.days().length));

  protected readonly hasRefusals = computed(() => this.days().some((day) => day.rejected > 0));

  constructor() {
    void this.store.load();
  }

  protected async choose(preset: RangePreset): Promise<void> {
    await this.store.choose(preset);
  }

  protected showsLabel(index: number): boolean {
    const days = this.days();

    // The first, the last, every nth — and the first of any month however the thinning falls,
    // because that is the label that says which month the numbers either side of it belong to.
    return (
      index === 0 ||
      index === days.length - 1 ||
      index % this.labelStep() === 0 ||
      startsMonth(days[index].date)
    );
  }

  protected dayLabel(date: string): string {
    return dayLabel(date);
  }

  protected shortDayLabel(date: string): string {
    return shortDayLabel(date);
  }

  protected reasonLabel(reason: RejectionBreakdown['reason']): string {
    return reasonLabel(reason);
  }

  /** Grouped, because $3104.00 is read digit by digit and $3,104.00 is read at a glance. */
  protected money(value: number): string {
    return `$${value.toLocaleString(undefined, {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })}`;
  }

  protected percent(fraction: number): string {
    return `${(fraction * 100).toFixed(1)}%`;
  }

  /** The tooltip for a hovered day — both charts show the same one, so it names both measures. */
  protected tooltip(bar: Bar): string {
    const refused = bar.rejectedCount > 0 ? `, ${bar.rejectedCount} refused` : '';
    return `${dayLabel(bar.date)}: ${bar.orders} order${bar.orders === 1 ? '' : 's'}${refused} · ${this.money(bar.revenueUsd)}`;
  }
}
