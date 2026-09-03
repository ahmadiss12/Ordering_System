import { Injectable, signal } from '@angular/core';

const ENABLED_KEY = 'ordering.queue.chime';

/**
 * The sound a new order makes.
 *
 * A kitchen screen is propped up across the room and nobody is watching it. Colour tells a person
 * what needs attention once they look; this is what makes them look.
 *
 * Synthesised rather than played from a file, which is not cleverness for its own sake: it is two
 * hundred bytes instead of an asset to load over a Lebanese connection, it cannot 404, and it
 * plays the instant it is asked rather than after a fetch.
 *
 * Browsers refuse to make noise until the page has been interacted with, and a tablet showing a
 * restored session has had no interaction at all. So this reports whether it is actually able to
 * play, and the screen offers a way to turn it on rather than pretending silence is a setting.
 */
@Injectable({ providedIn: 'root' })
export class Chime {
  private context: AudioContext | null = null;

  /** Whether the person wants a sound at all. Remembered per device, on by default. */
  readonly enabled = signal(readStoredPreference());

  /** True when the browser has let us make noise. False until somebody has tapped something. */
  readonly ready = signal(false);

  setEnabled(enabled: boolean): void {
    this.enabled.set(enabled);

    try {
      localStorage.setItem(ENABLED_KEY, String(enabled));
    } catch {
      // A privacy mode that refuses storage. The setting simply will not survive a reload.
    }

    if (enabled) {
      void this.unlock();
    }
  }

  /**
   * Called from a real press, which is the only moment a browser will start an audio context.
   * Safe to call repeatedly.
   */
  async unlock(): Promise<void> {
    try {
      this.context ??= new AudioContext();

      if (this.context.state === 'suspended') {
        await this.context.resume();
      }

      this.ready.set(this.context.state === 'running');
    } catch {
      // No Web Audio at all. The board still works; it is just quiet.
      this.ready.set(false);
    }
  }

  /** Two rising notes. Short, so it cuts through a kitchen without becoming something to resent. */
  play(): void {
    if (!this.enabled() || !this.context || this.context.state !== 'running') {
      return;
    }

    const now = this.context.currentTime;
    this.note(880, now, 0.12);
    this.note(1318.5, now + 0.13, 0.18);
  }

  private note(frequency: number, at: number, duration: number): void {
    const context = this.context;
    if (!context) {
      return;
    }

    const oscillator = context.createOscillator();
    const gain = context.createGain();

    oscillator.type = 'sine';
    oscillator.frequency.value = frequency;

    // Ramped rather than switched. A gain that jumps to zero clicks, and a click in a quiet
    // kitchen is more noticeable than the note.
    gain.gain.setValueAtTime(0.0001, at);
    gain.gain.exponentialRampToValueAtTime(0.2, at + 0.01);
    gain.gain.exponentialRampToValueAtTime(0.0001, at + duration);

    oscillator.connect(gain).connect(context.destination);
    oscillator.start(at);
    oscillator.stop(at + duration + 0.02);
  }
}

function readStoredPreference(): boolean {
  try {
    // Absent means on. A kitchen that has never chosen wants to hear about a new order.
    return localStorage.getItem(ENABLED_KEY) !== 'false';
  } catch {
    return true;
  }
}
