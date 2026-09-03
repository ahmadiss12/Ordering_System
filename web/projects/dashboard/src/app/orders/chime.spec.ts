import { TestBed } from '@angular/core/testing';
import { Chime } from './chime';

/**
 * The sound a new order makes, and the honesty around whether it can make it.
 *
 * The tone itself is not tested — asserting on oscillator frequencies would be testing Web Audio
 * rather than anything decided here. What matters is that the setting survives, that silence is
 * never mistaken for a working sound, and that a browser refusing audio does not take the board
 * down with it.
 */
describe('Chime', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
  });

  afterEach(() => localStorage.clear());

  it('is on for a kitchen that has never chosen', () => {
    // A tablet nobody has configured should still shout when an order arrives. Defaulting to
    // silence would mean the feature exists and nobody knows.
    expect(TestBed.inject(Chime).enabled()).toBe(true);
  });

  it('remembers being turned off', () => {
    TestBed.inject(Chime).setEnabled(false);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});

    expect(TestBed.inject(Chime).enabled()).toBe(false);
  });

  it('does not claim to be ready before the browser has allowed it', () => {
    // Browsers refuse audio until the page has been interacted with, and a tablet showing a
    // restored session has had no interaction. A toggle that read "on" while the page was silent
    // would be a lie the screen tells every shift.
    expect(TestBed.inject(Chime).ready()).toBe(false);
  });

  it('stays quiet rather than throwing when it has not been unlocked', () => {
    // Called from an effect on every new order. Throwing here would take the board with it.
    expect(() => TestBed.inject(Chime).play()).not.toThrow();
  });

  it('survives a browser with no Web Audio at all', async () => {
    const original = globalThis.AudioContext;

    try {
      // @ts-expect-error deliberately removing it, which is what an old or locked-down browser
      // looks like from here.
      delete globalThis.AudioContext;

      const chime = TestBed.inject(Chime);
      await chime.unlock();

      expect(chime.ready()).toBe(false);
      expect(() => chime.play()).not.toThrow();
    } finally {
      globalThis.AudioContext = original;
    }
  });

  it('survives storage that refuses to be written', () => {
    const original = Storage.prototype.setItem;

    try {
      Storage.prototype.setItem = () => {
        throw new Error('storage is disabled in this mode');
      };

      const chime = TestBed.inject(Chime);
      expect(() => chime.setEnabled(false)).not.toThrow();

      // The setting still holds for this session; it simply will not survive a reload.
      expect(chime.enabled()).toBe(false);
    } finally {
      Storage.prototype.setItem = original;
    }
  });
});
