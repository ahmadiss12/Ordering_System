import { Injectable, computed, inject, signal } from '@angular/core';
import { DayOfWeek, OpeningWindow, RestaurantHoursClient, describeError } from 'api-client';
import { firstValueFrom } from 'rxjs';

/** Monday first: a week of opening hours is read starting there, not on Sunday as the enum numbers it. */
export const WEEK: readonly { day: DayOfWeek; label: string }[] = [
  { day: DayOfWeek.Monday, label: 'Monday' },
  { day: DayOfWeek.Tuesday, label: 'Tuesday' },
  { day: DayOfWeek.Wednesday, label: 'Wednesday' },
  { day: DayOfWeek.Thursday, label: 'Thursday' },
  { day: DayOfWeek.Friday, label: 'Friday' },
  { day: DayOfWeek.Saturday, label: 'Saturday' },
  { day: DayOfWeek.Sunday, label: 'Sunday' },
];

/** One window as the form holds it: "HH:mm", which is what an `input type="time"` speaks. */
export interface DraftWindow {
  open: string;
  close: string;
}

export interface DraftDay {
  readonly day: DayOfWeek;
  readonly label: string;
  readonly windows: DraftWindow[];
}

/**
 * A week of opening hours, being edited.
 *
 * <h4>Why there is a draft at all</h4>
 *
 * The API replaces the whole week in one write, and refuses a week whose windows overlap. So the
 * screen has to hold a complete week that is allowed to be briefly wrong — half-typed, or two
 * windows that clash until the second is finished — and send it only when somebody says so.
 * Saving each row as it changed would refuse edits that were on their way to being valid.
 */
@Injectable()
export class HoursStore {
  private readonly client = inject(RestaurantHoursClient);

  private readonly draftSignal = signal<DraftDay[]>(emptyWeek());
  private readonly savedSignal = signal<string>('');

  readonly draft = this.draftSignal.asReadonly();
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly isOpenNow = signal(false);

  /**
   * Whether the week on screen came from the server.
   *
   * False until a load succeeds, because the draft starts as seven empty days — and seven rows
   * saying "Closed" is a confident answer. A failed load that drew them anyway would tell an
   * owner their restaurant is shut all week, under an error message they might not read.
   */
  readonly loaded = signal(false);

  /** True while the draft differs from what the server last confirmed. */
  readonly dirty = computed(() => serialise(this.draftSignal()) !== this.savedSignal());

  /** No windows anywhere: the restaurant would be shut to customers until some come back. */
  readonly wouldCloseIndefinitely = computed(() =>
    this.draftSignal().every((day) => day.windows.length === 0),
  );

  async load(): Promise<void> {
    this.loading.set(true);

    try {
      const week = await firstValueFrom(this.client.get());

      this.replaceDraft(fromApi(week.windows));
      this.isOpenNow.set(week.isOpenNow);
      this.error.set(null);
      this.loaded.set(true);
    } catch (error) {
      this.error.set(describeError(error, 'Could not load your opening hours.'));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Sends the week. Returns whether it saved, so a caller knows whether the confirmation it
   * showed was worth showing.
   */
  async save(confirmClosedIndefinitely = false): Promise<boolean> {
    this.saving.set(true);
    this.error.set(null);

    try {
      const week = await firstValueFrom(
        this.client.set({
          windows: toApi(this.draftSignal()),
          confirmClosedIndefinitely,
        }),
      );

      // Replaced from the response rather than from the draft: the server sorts the week and is
      // the authority on what is stored, so the screen shows what was actually saved.
      this.replaceDraft(fromApi(week.windows));
      this.isOpenNow.set(week.isOpenNow);
      return true;
    } catch (error) {
      this.error.set(describeError(error, 'Could not save your opening hours.'));
      return false;
    } finally {
      this.saving.set(false);
    }
  }

  addWindow(day: DayOfWeek): void {
    this.draftSignal.update((week) =>
      week.map((d) =>
        d.day === day
          ? // Continuing the day rather than starting at midnight: a second window is almost
            // always a dinner sitting after a lunch one.
            { ...d, windows: [...d.windows, nextWindow(d.windows)] }
          : d,
      ),
    );
  }

  removeWindow(day: DayOfWeek, index: number): void {
    this.draftSignal.update((week) =>
      week.map((d) =>
        d.day === day ? { ...d, windows: d.windows.filter((_, i) => i !== index) } : d,
      ),
    );
  }

  setTime(day: DayOfWeek, index: number, field: 'open' | 'close', value: string): void {
    this.draftSignal.update((week) =>
      week.map((d) =>
        d.day === day
          ? {
              ...d,
              windows: d.windows.map((w, i) => (i === index ? { ...w, [field]: value } : w)),
            }
          : d,
      ),
    );
  }

  /**
   * Gives every day the first day's windows.
   *
   * Most restaurants keep the same hours all week, so this is the difference between filling in
   * one day and filling in seven. It copies Monday because Monday is the row at the top.
   */
  copyFirstDayToAll(): void {
    this.draftSignal.update((week) => {
      const source = week[0].windows;
      return week.map((d) => ({ ...d, windows: source.map((w) => ({ ...w })) }));
    });
  }

  /** Throws the edit away and goes back to what the server last confirmed. */
  discard(): void {
    this.replaceDraft(parse(this.savedSignal()));
  }

  private replaceDraft(week: DraftDay[]): void {
    this.draftSignal.set(week);
    this.savedSignal.set(serialise(week));
  }
}

/** A window that runs past midnight — the close time is earlier than the open time. */
export function isOvernight(window: DraftWindow): boolean {
  return !!window.open && !!window.close && window.close < window.open;
}

function emptyWeek(): DraftDay[] {
  return WEEK.map((d) => ({ day: d.day, label: d.label, windows: [] }));
}

function fromApi(windows: readonly OpeningWindow[]): DraftDay[] {
  return WEEK.map((d) => ({
    day: d.day,
    label: d.label,
    windows: windows
      .filter((w) => w.day === d.day)
      .map((w) => ({ open: hhmm(w.openTime), close: hhmm(w.closeTime) })),
  }));
}

function toApi(week: readonly DraftDay[]): OpeningWindow[] {
  return week.flatMap((d) =>
    d.windows
      // A half-typed row is not a window. Sending it would earn a validation error for a row the
      // person has not finished rather than for anything they did wrong.
      .filter((w) => w.open && w.close)
      .map((w) => ({ day: d.day, openTime: `${w.open}:00`, closeTime: `${w.close}:00` })),
  );
}

/** "09:00:00" from the API, "09:00" in an input. Seconds are never part of an opening time. */
function hhmm(time: string): string {
  return time.slice(0, 5);
}

/** Where a new window starts: after the last one, or at noon on an empty day. */
function nextWindow(existing: readonly DraftWindow[]): DraftWindow {
  const last = existing[existing.length - 1];
  return last?.close ? { open: last.close, close: '' } : { open: '12:00', close: '' };
}

function serialise(week: readonly DraftDay[]): string {
  return JSON.stringify(week.map((d) => [d.day, d.windows]));
}

function parse(serialised: string): DraftDay[] {
  const rows = JSON.parse(serialised) as [DayOfWeek, DraftWindow[]][];
  return WEEK.map((d, i) => ({ day: d.day, label: d.label, windows: rows[i]?.[1] ?? [] }));
}
