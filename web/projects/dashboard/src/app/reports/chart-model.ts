import { ReportDay } from 'api-client';

/**
 * Turns a report's days into the geometry two small-multiple bar charts need.
 *
 * <h4>Two charts rather than one with two scales</h4>
 *
 * Orders and revenue are measures of different size — twelve orders and three hundred dollars —
 * and drawing them against two y-axes on one plot invents whatever correlation the chosen scales
 * imply. They share the day axis and nothing else.
 */

/** The plotting box, in the SVG's own units. Rendered responsively by a viewBox. */
export const CHART = {
  width: 720,
  height: 132,
  top: 8,
  bottom: 18,
  left: 40,
  right: 4,
  /** A surface-coloured gap between neighbouring bars and between stacked segments. */
  gap: 2,
  /** Where the day labels sit, below the baseline of whichever plot draws them. */
  labelBaseline: 128,
  /** Rounded data-ends, anchored to the baseline so only the far end is rounded. */
  radius: 4,
} as const;

export interface Bar {
  readonly date: string;
  readonly x: number;
  readonly width: number;
  /** Stacked from the baseline up: the kept part, then the refused part on top of it. */
  readonly kept: Segment | null;
  readonly rejected: Segment | null;
  readonly orders: number;
  readonly rejectedCount: number;
  readonly revenueUsd: number;
  readonly commissionUsd: number;
}

export interface Segment {
  readonly y: number;
  readonly height: number;
}

export interface Tick {
  readonly value: number;
  readonly y: number;
  readonly label: string;
}

export interface Axis {
  readonly bars: readonly Bar[];
  readonly ticks: readonly Tick[];
  readonly max: number;
  readonly baseline: number;
}

/** Orders per day, each bar split into what was kept and what was refused. */
export function ordersAxis(days: readonly ReportDay[]): Axis {
  const max = niceMax(Math.max(1, ...days.map((d) => d.orders)));
  const baseline = CHART.height - CHART.bottom;
  const plot = baseline - CHART.top;
  const { width, step } = bandWidth(days.length);

  const bars = days.map((day, index) => {
    const keptCount = day.orders - day.rejected;
    const x = CHART.left + index * step;

    // Heights before positions, so the 2px separator comes out of the lower segment rather than
    // pushing the stack above its own total.
    const rejectedHeight = (day.rejected / max) * plot;
    const keptHeight = (keptCount / max) * plot;
    const separator = day.rejected > 0 && keptCount > 0 ? CHART.gap : 0;

    return {
      date: day.date,
      x,
      width,
      kept:
        keptCount > 0
          ? { y: baseline - keptHeight, height: Math.max(1, keptHeight - separator) }
          : null,
      rejected:
        day.rejected > 0
          ? { y: baseline - keptHeight - rejectedHeight, height: Math.max(1, rejectedHeight) }
          : null,
      orders: day.orders,
      rejectedCount: day.rejected,
      revenueUsd: day.revenueUsd,
      commissionUsd: day.commissionUsd,
    };
  });

  return { bars, ticks: ticksFor(max, baseline, plot, (v) => `${v}`), max, baseline };
}

/** Revenue per day. One series, so no legend — the chart's own heading names it. */
export function revenueAxis(days: readonly ReportDay[]): Axis {
  const max = niceMax(Math.max(1, ...days.map((d) => d.revenueUsd)));
  const baseline = CHART.height - CHART.bottom;
  const plot = baseline - CHART.top;
  const { width, step } = bandWidth(days.length);

  const bars = days.map((day, index) => {
    const height = (day.revenueUsd / max) * plot;

    return {
      date: day.date,
      x: CHART.left + index * step,
      width,
      kept: day.revenueUsd > 0 ? { y: baseline - height, height: Math.max(1, height) } : null,
      rejected: null,
      orders: day.orders,
      rejectedCount: day.rejected,
      revenueUsd: day.revenueUsd,
      commissionUsd: day.commissionUsd,
    };
  });

  return { bars, ticks: ticksFor(max, baseline, plot, money), max, baseline };
}

/** A bar's width and the distance to the next one, with the gap taken out of the bar. */
function bandWidth(count: number): { width: number; step: number } {
  const step = (CHART.width - CHART.left - CHART.right) / Math.max(1, count);
  return { width: Math.max(1, step - CHART.gap), step };
}

/**
 * A round number at or above the largest value, so the axis reads 0/5/10 rather than 0/3.5/7.
 *
 * <p>
 * The step list is fine on purpose. A coarse one (1, 2, 2.5, 5, 10) sounds tidier and is what
 * this had at first: a busiest day of 27 orders went to an axis of 50, and every bar sat in the
 * bottom half of the plot with the day-to-day differences flattened out of sight. Half a chart of
 * empty space is a worse trade than an axis topping out at 30.
 * </p>
 */
function niceMax(value: number): number {
  const magnitude = 10 ** Math.floor(Math.log10(value));
  // No gap between neighbours wider than a third, so the worst case leaves a third of the plot
  // as headroom rather than half of it. 1 to 1.5 was the gap that let 1,100 round up to 1,500.
  const steps = [1, 1.25, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10];

  for (const step of steps) {
    const candidate = magnitude * step;
    if (candidate >= value) {
      return candidate;
    }
  }

  return magnitude * 10;
}

function ticksFor(
  max: number,
  baseline: number,
  plot: number,
  label: (value: number) => string,
): Tick[] {
  return [0, 0.5, 1].map((fraction) => ({
    value: max * fraction,
    y: baseline - plot * fraction,
    label: label(max * fraction),
  }));
}

/** Compact on an axis: $1.2k rather than $1,200, which would need a wider gutter than the chart. */
function money(value: number): string {
  if (value >= 1000) {
    return `$${(value / 1000).toFixed(value % 1000 === 0 ? 0 : 1)}k`;
  }

  return `$${Math.round(value)}`;
}

/** "Mon 5 Sep" — parsed by hand, because `new Date('2026-09-05')` is midnight UTC and a browser
 *  west of Greenwich renders it as the 4th. */
export function dayLabel(date: string): string {
  const parsed = new Date(+date.slice(0, 4), +date.slice(5, 7) - 1, +date.slice(8, 10));

  return parsed.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short' });
}

/**
 * Just the day number, for an axis where thirty of them have to fit — except on the first day of
 * a month, which carries its month too.
 *
 * Without that, a range crossing a month end reads "…28, 31, 3, 5" and there is nothing on screen
 * saying where one month stopped and the next began.
 */
export function shortDayLabel(date: string): string {
  const day = +date.slice(8, 10);

  if (day !== 1) {
    return `${day}`;
  }

  const month = new Date(+date.slice(0, 4), +date.slice(5, 7) - 1, 1);
  return month.toLocaleDateString(undefined, { month: 'short' });
}

/**
 * How many day labels to draw, so thirty do not overlap into a smear. Every nth, always including
 * the first and last.
 */
export function labelEvery(count: number): number {
  return Math.max(1, Math.ceil(count / 10));
}

/** Whether a day starts a month, so the axis can always name it however the thinning falls. */
export function startsMonth(date: string): boolean {
  return date.slice(8, 10) === '01';
}
