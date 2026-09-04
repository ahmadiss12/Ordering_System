import { TestBed } from '@angular/core/testing';
import { DayOfWeek, OpeningWindow, RestaurantHoursClient, WeeklyHoursResponse } from 'api-client';
import { Observable, of, throwError } from 'rxjs';
import { HoursStore, isOvernight } from './hours-store';

/**
 * The week being edited.
 *
 * What matters here is the translation either side of the form — "09:00:00" from the API against
 * "09:00" in an input — and the two states the API refuses: a half-typed window, and a week
 * emptied without saying so.
 */
describe('HoursStore', () => {
  let client: FakeHoursClient;

  beforeEach(() => {
    client = new FakeHoursClient();
    TestBed.configureTestingModule({
      providers: [HoursStore, { provide: RestaurantHoursClient, useValue: client }],
    });
  });

  it('lays the week out Monday first', async () => {
    const store = await loaded();

    // DayOfWeek numbers Sunday zero. Sorting by the enum would put Sunday at the top of a screen
    // nobody reads that way.
    expect(store.draft().map((d) => d.label)).toEqual([
      'Monday',
      'Tuesday',
      'Wednesday',
      'Thursday',
      'Friday',
      'Saturday',
      'Sunday',
    ]);
  });

  it('drops the seconds the API sends and puts them back on the way out', async () => {
    client.returns([window(DayOfWeek.Monday, '09:00:00', '17:00:00')]);
    const store = await loaded();

    // An input type=time speaks "09:00"; the API speaks "09:00:00". Seconds are never part of an
    // opening time, and showing them would invite somebody to edit them.
    expect(store.draft()[0].windows).toEqual([{ open: '09:00', close: '17:00' }]);

    await store.save();
    expect(client.lastSent).toEqual([
      { day: DayOfWeek.Monday, openTime: '09:00:00', closeTime: '17:00:00' },
    ]);
  });

  it('does not send a window somebody has not finished typing', async () => {
    const store = await loaded();

    store.addWindow(DayOfWeek.Monday);
    await store.save();

    // A new row has an open time and no close time. Sending it would earn a validation error for
    // a row on its way to being right.
    expect(client.lastSent).toEqual([]);
  });

  it('knows when nothing is left', async () => {
    client.returns([window(DayOfWeek.Monday, '09:00:00', '17:00:00')]);
    const store = await loaded();

    expect(store.wouldCloseIndefinitely()).toBe(false);

    store.removeWindow(DayOfWeek.Monday, 0);
    expect(store.wouldCloseIndefinitely()).toBe(true);
  });

  it('only claims to be dirty once something changed', async () => {
    client.returns([window(DayOfWeek.Monday, '09:00:00', '17:00:00')]);
    const store = await loaded();

    expect(store.dirty()).toBe(false);

    store.setTime(DayOfWeek.Monday, 0, 'close', '18:00');
    expect(store.dirty()).toBe(true);
  });

  it('puts the week back when an edit is discarded', async () => {
    client.returns([window(DayOfWeek.Monday, '09:00:00', '17:00:00')]);
    const store = await loaded();

    store.setTime(DayOfWeek.Monday, 0, 'close', '18:00');
    store.addWindow(DayOfWeek.Friday);

    store.discard();

    expect(store.dirty()).toBe(false);
    expect(store.draft()[0].windows).toEqual([{ open: '09:00', close: '17:00' }]);
    expect(store.draft()[4].windows).toEqual([]);
  });

  it('starts a second window where the first one ended', async () => {
    client.returns([window(DayOfWeek.Monday, '12:00:00', '16:00:00')]);
    const store = await loaded();

    store.addWindow(DayOfWeek.Monday);

    // A second window is almost always a dinner sitting after a lunch one, so it continues the
    // day rather than starting at midnight.
    expect(store.draft()[0].windows[1].open).toBe('16:00');
  });

  it('copies Monday to every day, because most weeks are one day repeated', async () => {
    client.returns([window(DayOfWeek.Monday, '10:00:00', '23:00:00')]);
    const store = await loaded();

    store.copyFirstDayToAll();

    expect(store.draft().every((d) => d.windows[0]?.open === '10:00')).toBe(true);

    await store.save();
    expect(client.lastSent).toHaveLength(7);
  });

  it('says so when a save is refused, and keeps the edit', async () => {
    client.returns([window(DayOfWeek.Monday, '09:00:00', '17:00:00')]);
    const store = await loaded();

    store.setTime(DayOfWeek.Monday, 0, 'close', '18:00');
    client.fails();

    expect(await store.save()).toBe(false);
    expect(store.error()).not.toBeNull();
    // Throwing the edit away on a failed save would lose work somebody has just done.
    expect(store.draft()[0].windows[0].close).toBe('18:00');
  });

  it('does not pretend to know the week when the load failed', async () => {
    client.failsToLoad();
    const store = TestBed.inject(HoursStore);
    await store.load();

    // The draft starts as seven empty days, and seven rows saying "Closed" is a confident answer.
    // The screen draws nothing until this is true, so a failed load cannot tell an owner their
    // restaurant is shut all week under an error message they might not read.
    expect(store.loaded()).toBe(false);
    expect(store.error()).not.toBeNull();
  });

  it('recognises a window that runs past midnight', () => {
    expect(isOvernight({ open: '12:00', close: '02:00' })).toBe(true);
    expect(isOvernight({ open: '09:00', close: '17:00' })).toBe(false);

    // Half-typed is not overnight. Saying "closes 00:00 the next day" under an empty box would be
    // a confident answer to a question nobody asked.
    expect(isOvernight({ open: '12:00', close: '' })).toBe(false);
  });

  async function loaded(): Promise<HoursStore> {
    const store = TestBed.inject(HoursStore);
    await store.load();
    return store;
  }

  function window(day: DayOfWeek, openTime: string, closeTime: string): OpeningWindow {
    return { day, openTime, closeTime } as OpeningWindow;
  }
});

class FakeHoursClient {
  lastSent: OpeningWindow[] | undefined;

  private windows: OpeningWindow[] = [];
  private failing = false;
  private failingLoad = false;

  returns(windows: OpeningWindow[]): void {
    this.windows = windows;
  }

  fails(): void {
    this.failing = true;
  }

  failsToLoad(): void {
    this.failingLoad = true;
  }

  get(): Observable<WeeklyHoursResponse> {
    if (this.failingLoad) {
      return throwError(() => new Error('the API is not answering'));
    }

    return of(this.week());
  }

  set(body: { windows: OpeningWindow[] }): Observable<WeeklyHoursResponse> {
    if (this.failing) {
      return throwError(() => new Error('two windows overlap'));
    }

    this.lastSent = body.windows;
    this.windows = body.windows;

    return of(this.week());
  }

  private week(): WeeklyHoursResponse {
    return {
      windows: this.windows,
      isOpenNow: this.windows.length > 0,
      isClosedIndefinitely: this.windows.length === 0,
    } as WeeklyHoursResponse;
  }
}
