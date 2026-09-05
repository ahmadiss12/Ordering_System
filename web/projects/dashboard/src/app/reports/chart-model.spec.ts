import { ReportDay } from 'api-client';
import { CHART, dayLabel, labelEvery, ordersAxis, revenueAxis, shortDayLabel } from './chart-model';

/**
 * The geometry behind the two plots.
 *
 * <p>
 * Worth testing on its own because it is arithmetic with a visual failure mode: a bar drawn one
 * pixel too tall, or a stack whose two halves overlap, is not something a passing render test
 * would notice.
 * </p>
 */
describe('chart geometry', () => {
  it('scales bars against the tallest day, not against each bar', () => {
    const axis = ordersAxis([day('2026-09-01', 10), day('2026-09-02', 5)]);

    // Half the orders, half the height. A bar scaled to its own value would draw both full.
    expect(axis.bars[1].kept!.height).toBeCloseTo(axis.bars[0].kept!.height / 2, 5);
  });

  it('rounds the axis up to a readable number', () => {
    // Twenty-three orders reads against 0/12.5/25, not against 0/11.5/23.
    expect(ordersAxis([day('2026-09-01', 23)]).max).toBe(25);
    expect(ordersAxis([day('2026-09-01', 7)]).max).toBe(8);
  });

  it('does not leave half the plot empty to get a rounder number', () => {
    // The failure this pins was visible only on screen: a coarse step list sent a busiest day of
    // 27 to an axis of 50, and every bar in the month sat in the bottom half with the day-to-day
    // differences flattened away. Round is worth something; half a chart of white space is not.
    for (const value of [3, 7, 12, 23, 27, 41, 96, 260, 1_100]) {
      const max = ordersAxis([day('2026-09-01', value)]).max;

      expect(max).toBeGreaterThanOrEqual(value);
      expect(max).toBeLessThan(value * 1.35);
    }
  });

  it('never draws an axis of zero for an empty period', () => {
    const axis = ordersAxis([day('2026-09-01', 0)]);

    // A zero maximum would divide by zero and put every bar at NaN.
    expect(axis.max).toBeGreaterThan(0);
    expect(axis.bars[0].kept).toBeNull();
  });

  it('stacks the refused part on top of the kept part without overlapping it', () => {
    const axis = ordersAxis([day('2026-09-01', 10, 4)]);
    const bar = axis.bars[0];

    // The refused segment sits entirely above the kept one, with the 2px separator between them.
    const keptTop = bar.kept!.y;
    const refusedBottom = bar.rejected!.y + bar.rejected!.height;

    expect(refusedBottom).toBeLessThanOrEqual(keptTop + 0.001);
    expect(keptTop - refusedBottom).toBeCloseTo(0, 5);
    expect(bar.kept!.y + bar.kept!.height).toBeCloseTo(axis.baseline - CHART.gap, 5);
  });

  it('keeps the stack inside the plot rather than growing it by the separator', () => {
    const axis = ordersAxis([day('2026-09-01', 10, 4)]);
    const bar = axis.bars[0];

    // The gap comes out of the lower segment. Taking it from neither would push the stack above
    // the height its own total earns, and the tallest day would overflow the chart.
    expect(bar.rejected!.y).toBeGreaterThanOrEqual(CHART.top - 0.001);
  });

  it('draws a day with no refusals as one bar', () => {
    const bar = ordersAxis([day('2026-09-01', 6)]).bars[0];

    expect(bar.rejected).toBeNull();
    expect(bar.kept!.y + bar.kept!.height).toBeCloseTo(
      ordersAxis([day('2026-09-01', 6)]).baseline,
      5,
    );
  });

  it('leaves a gap between neighbouring bars', () => {
    const bars = ordersAxis([day('2026-09-01', 3), day('2026-09-02', 3)]).bars;

    expect(bars[1].x - (bars[0].x + bars[0].width)).toBeCloseTo(CHART.gap, 5);
  });

  it('gives revenue its own scale rather than sharing the orders one', () => {
    const days = [day('2026-09-01', 2, 0, 300)];

    // Two orders worth three hundred dollars. Sharing one axis is what makes a chart invent a
    // relationship that is not in the data.
    expect(ordersAxis(days).max).toBe(2);
    expect(revenueAxis(days).max).toBe(300);
  });

  it('reads a date without letting a timezone shift it', () => {
    // new Date('2026-09-05') is midnight UTC, which renders as the 4th anywhere west of
    // Greenwich. These parse the parts by hand for exactly that reason.
    expect(shortDayLabel('2026-09-05')).toBe('5');
    expect(dayLabel('2026-09-05')).toContain('5');
  });

  it('names the month on its first day, so a range that crosses one is readable', () => {
    // Otherwise a month-end range reads "…28, 31, 3, 5" with nothing saying where one month
    // stopped and the next started.
    expect(shortDayLabel('2026-09-01')).toMatch(/sep/i);
    expect(shortDayLabel('2026-09-02')).toBe('2');
  });

  it('thins the day labels so a month does not smear', () => {
    expect(labelEvery(7)).toBe(1);
    expect(labelEvery(30)).toBe(3);
    expect(labelEvery(90)).toBe(9);
  });
});

function day(date: string, orders: number, rejected = 0, revenueUsd = 0): ReportDay {
  return { date, orders, rejected, revenueUsd, commissionUsd: 0 } as ReportDay;
}
