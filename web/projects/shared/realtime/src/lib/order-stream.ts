import { DestroyRef, Injectable, effect, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { API_BASE_URL } from 'api-client';
import { SessionStore, TokenRefresher, decodeJwt } from 'auth';
import { firstValueFrom } from 'rxjs';
import { LiveStatus, OrderChanged } from './order-changed';
import { HUB_CONNECTION_FACTORY } from './hub-connection.factory';

/** Where the API mounts the hub, relative to wherever the API is. */
const HUB_PATH = '/hubs/orders';

/**
 * How often a screen is told to refetch while the channel is up. A backstop, not the mechanism:
 * a push that failed on the server is swallowed and logged there, so without this the screen
 * would never learn about the one order it missed.
 */
const BACKSTOP_MS = 60_000;

/** How often while the channel is down. This is the actual fallback, so it is much faster. */
const FALLBACK_MS = 10_000;

/** A token with less than this left on it is exchanged before the handshake rather than after. */
const EXPIRY_SKEW_SECONDS = 30;

/**
 * The live channel between the orders hub and a screen that has to stay current.
 *
 * <h4>One signal, not two mechanisms</h4>
 *
 * A screen watches {@link revision} and refetches whenever it changes. That number goes up on a
 * pushed message, on every poll tick, and the moment a dropped connection comes back — so "live
 * updates" and "polling fallback" are one thing a screen consumes rather than two code paths it
 * has to keep in agreement. The screen never asks whether the socket is up; it only ever asks
 * whether it is behind.
 *
 * The bump on reconnect is the part that is easy to leave out and expensive to miss. A tablet
 * that loses signal for two minutes misses every message sent in those two minutes, and SignalR
 * does not replay them — a screen that only listened to pushes would come back looking current
 * and be wrong until the next order arrived.
 *
 * Nothing here fetches anything. What to refetch is the screen's business, and a stream that knew
 * about the orders query would have to be changed every time a screen wanted something different.
 *
 * <h4>Why the messages are also exposed</h4>
 *
 * {@link lastChange} carries the actual event, for the things a refetch cannot tell you: that
 * *this* order is the new one, worth a sound and a highlight, rather than that the list changed.
 */
@Injectable({ providedIn: 'root' })
export class OrderStream {
  private readonly session = inject(SessionStore);

  // The same setting the generated client resolves its URLs against, rather than a second one
  // that has to agree with it. The hub is served by the same ASP.NET application as the API, so
  // two settings could only ever be two ways to get it wrong. Empty means relative, which is
  // right in development, where the dev server proxies both to one origin.
  private readonly baseUrl = inject(API_BASE_URL, { optional: true }) ?? '';
  private readonly refresher = inject(TokenRefresher);
  private readonly connect = inject(HUB_CONNECTION_FACTORY);

  private connection: HubConnection | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;

  private readonly statusSignal = signal<LiveStatus>('off');
  private readonly revisionSignal = signal(0);
  private readonly lastChangeSignal = signal<OrderChanged | null>(null);

  /** Whether the channel is up. For a screen that wants to show it, not to decide with. */
  readonly status = this.statusSignal.asReadonly();

  /** Goes up whenever the screen should refetch. The only thing a screen needs to watch. */
  readonly revision = this.revisionSignal.asReadonly();

  /** The most recent push, or null when none has arrived on this connection. */
  readonly lastChange = this.lastChangeSignal.asReadonly();

  constructor() {
    // Follows the session rather than being started by a screen. Signing out has to close the
    // socket: SignalR would otherwise keep reconnecting with a token belonging to somebody who
    // has left, on a shared tablet where the next person is somebody else.
    effect(() => {
      if (this.session.isAuthenticated()) {
        void this.start();
      } else {
        void this.stop();
      }
    });

    inject(DestroyRef).onDestroy(() => void this.stop());
  }

  private async start(): Promise<void> {
    if (this.connection) {
      return;
    }

    const connection = this.connect(this.baseUrl + HUB_PATH, () => this.freshAccessToken());
    this.connection = connection;

    connection.on('orderChanged', (change: OrderChanged) => {
      this.lastChangeSignal.set(change);
      this.bump();
    });

    connection.onreconnecting(() => this.statusSignal.set('reconnecting'));

    connection.onreconnected(() => {
      this.statusSignal.set('live');

      // Everything sent while it was down is gone — SignalR replays nothing — so the screen is
      // told to refetch rather than being left looking current and being wrong.
      this.bump();
    });

    // Not onclose -> start(). withAutomaticReconnect owns retrying, and a second loop on top of
    // it would race with it. onclose here means it has been stopped deliberately.
    connection.onclose(() => this.statusSignal.set('off'));

    this.restartTimer(FALLBACK_MS);
    this.statusSignal.set('connecting');

    try {
      await connection.start();
      this.statusSignal.set('live');
      this.restartTimer(BACKSTOP_MS);
    } catch {
      // The first connect failed, so automatic reconnect never engages — it only covers a
      // connection that was once up. The poll is already running and is what keeps the screen
      // current until start() succeeds on a later attempt.
      this.statusSignal.set('reconnecting');
      this.scheduleRetry();
    }
  }

  private scheduleRetry(): void {
    setTimeout(() => {
      if (
        this.connection?.state === HubConnectionState.Disconnected &&
        this.session.isAuthenticated()
      ) {
        void this.connection.start().then(
          () => {
            this.statusSignal.set('live');
            this.restartTimer(BACKSTOP_MS);
            this.bump();
          },
          () => this.scheduleRetry(),
        );
      }
    }, FALLBACK_MS);
  }

  private async stop(): Promise<void> {
    const connection = this.connection;
    this.connection = null;

    this.clearTimer();
    this.statusSignal.set('off');
    this.lastChangeSignal.set(null);

    // Swallowed: stopping a connection that never opened throws, and there is nothing a caller
    // could do about a socket that is going away anyway.
    await connection?.stop().catch(() => undefined);
  }

  private bump(): void {
    this.revisionSignal.update((n) => n + 1);
  }

  private restartTimer(intervalMs: number): void {
    this.clearTimer();
    this.timer = setInterval(() => this.bump(), intervalMs);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  /**
   * A token that will still be valid when the handshake reaches the server.
   *
   * The HTTP interceptor can afford to send a stale token and recover from the 401, because it
   * can replay the request. A WebSocket handshake cannot: it fails, and SignalR treats that as a
   * connection failure rather than as something to refresh and retry. So expiry is checked here,
   * before the attempt rather than after it.
   */
  private async freshAccessToken(): Promise<string> {
    const token = this.session.accessToken();

    if (token && !expiresSoon(token)) {
      return token;
    }

    try {
      // Shared with every other refresh in flight, so a reconnect during a burst of expiring
      // requests does not spend a rotating refresh token twice and end the session.
      return await firstValueFrom(this.refresher.refresh());
    } catch {
      // Empty means the handshake is refused and the reconnect loop tries again. Throwing here
      // would be swallowed by SignalR anyway, and this keeps the failure in one shape.
      return '';
    }
  }
}

function expiresSoon(token: string): boolean {
  const exp = decodeJwt(token)?.exp;

  // A token whose expiry cannot be read is treated as spent. Refreshing needlessly costs one
  // request; connecting with a dead token costs the screen its updates.
  return exp === undefined || exp - EXPIRY_SKEW_SECONDS <= Date.now() / 1000;
}
