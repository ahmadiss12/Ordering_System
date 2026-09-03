import { TestBed } from '@angular/core/testing';
import { Observable, of } from 'rxjs';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { SessionStore, TokenRefresher } from 'auth';
import { OrderStatus, provideApiClient } from 'api-client';
import { HUB_CONNECTION_FACTORY } from './hub-connection.factory';
import { OrderChanged } from './order-changed';
import { OrderStream } from './order-stream';

/**
 * The stream, and specifically the two things that make it worth having over a bare hub client:
 * one signal a screen watches whether the socket is up or down, and a bump on reconnect so a
 * tablet that lost signal comes back knowing it is behind.
 *
 * SignalR's own client is not exercised here — that is Microsoft's code and a real connection
 * needs a server. What is exercised is everything this file decides on top of it.
 */
describe('OrderStream', () => {
  let hub: FakeHub;
  let refresher: FakeRefresher;

  beforeEach(() => {
    vi.useFakeTimers();
    localStorage.clear();
    hub = new FakeHub();
    refresher = new FakeRefresher();

    configure();
  });

  function configure(extra: unknown[] = []): void {
    TestBed.configureTestingModule({
      providers: [
        { provide: TokenRefresher, useValue: refresher },
        {
          provide: HUB_CONNECTION_FACTORY,
          useValue: (url: string, accessToken: () => Promise<string>) => {
            hub.url = url;
            hub.accessToken = accessToken;
            return hub as unknown as HubConnection;
          },
        },
        ...(extra as never[]),
      ],
    });
  }

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
  });

  // ------------------------------------------------------------------ following the session

  it('stays off until somebody is signed in', () => {
    const stream = TestBed.inject(OrderStream);
    TestBed.tick();

    expect(hub.started).toBe(false);
    expect(stream.status()).toBe('off');
  });

  it('connects to the orders hub once somebody signs in', async () => {
    const stream = await connectedStream();

    expect(hub.url).toBe('/hubs/orders');
    expect(hub.started).toBe(true);
    expect(stream.status()).toBe('live');
  });

  it('resolves the hub against the same base url the generated client uses', async () => {
    // A deployed build that puts the API on another origin passes it once, to provideApiClient.
    // The hub is served by that same application, so it must follow rather than need its own
    // setting — two settings could only ever be two ways to get it wrong.
    TestBed.resetTestingModule();
    configure([provideApiClient('https://api.example.test')]);

    await connectedStream();

    expect(hub.url).toBe('https://api.example.test/hubs/orders');
  });

  it('closes the connection when the session ends', async () => {
    const stream = await connectedStream();

    // A shared tablet in a kitchen: the next person to pick it up is somebody else, and a socket
    // still reconnecting with the previous token is a live feed of orders to whoever is holding
    // it now.
    TestBed.inject(SessionStore).signOut();
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(0);

    expect(hub.stopped).toBe(true);
    expect(stream.status()).toBe('off');
  });

  // ------------------------------------------------------------------ what a screen watches

  it('bumps the revision when an order changes', async () => {
    const stream = await connectedStream();
    const before = stream.revision();

    hub.push(change(OrderStatus.Accepted, OrderStatus.Placed));

    expect(stream.revision()).toBe(before + 1);
    expect(stream.lastChange()?.status).toBe(OrderStatus.Accepted);
  });

  it('bumps the revision after a reconnection, because messages were missed', async () => {
    const stream = await connectedStream();

    hub.dropped();
    expect(stream.status()).toBe('reconnecting');

    const before = stream.revision();
    hub.recovered();

    // SignalR replays nothing. A screen that only listened to pushes would come back looking
    // current and be wrong until the next order happened to arrive.
    expect(stream.revision()).toBe(before + 1);
    expect(stream.status()).toBe('live');
  });

  it('keeps bumping on a timer while the channel is up', async () => {
    const stream = await connectedStream();
    const before = stream.revision();

    // A push the server failed to send is swallowed and logged there, so without this backstop
    // the screen never learns about the one order it missed.
    await vi.advanceTimersByTimeAsync(60_000);

    expect(stream.revision()).toBe(before + 1);
  });

  it('polls far faster while the channel is down', async () => {
    const stream = TestBed.inject(OrderStream);
    hub.failNextStart = true;

    signIn();
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(0);

    expect(stream.status()).toBe('reconnecting');

    const before = stream.revision();
    await vi.advanceTimersByTimeAsync(10_000);

    // Ten seconds, not sixty: this is the fallback rather than the backstop, and it is the only
    // thing keeping a kitchen screen current while the connection is gone.
    expect(stream.revision()).toBeGreaterThan(before);
  });

  it('reconnects by itself after a first connection that never succeeded', async () => {
    const stream = TestBed.inject(OrderStream);
    hub.failNextStart = true;

    signIn();
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(0);
    expect(stream.status()).toBe('reconnecting');

    // withAutomaticReconnect only covers a connection that was once up, so nothing in SignalR
    // retries this one. The API being down when a kitchen opens its tablet is not exotic.
    hub.state = HubConnectionState.Disconnected;
    await vi.advanceTimersByTimeAsync(10_000);

    expect(hub.started).toBe(true);
    expect(stream.status()).toBe('live');
  });

  // ------------------------------------------------------------------ the token

  it('hands the connection the stored token while it is still good', async () => {
    await connectedStream();

    expect(await hub.accessToken!()).toBe(freshToken);
  });

  it('exchanges an expiring token before the handshake rather than after it', async () => {
    TestBed.inject(OrderStream);

    signIn(expiredToken);
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(0);

    // A WebSocket handshake gets one attempt. There is no 401-then-retry the way there is for an
    // HTTP request, so a token that expired while the tablet was asleep has to be replaced first.
    //
    // Asserting the refreshed token by name, not merely that it differs: without the refresher
    // stubbed this test passed on the exchange *failing* and the catch returning an empty
    // string, which is the opposite of what it claims to check.
    expect(await hub.accessToken!()).toBe(refreshedToken);
    expect(refresher.calls).toBe(1);
  });

  it('does not exchange a token that is still good', async () => {
    await connectedStream();

    await hub.accessToken!();

    // Every reconnect calls the factory, and a tablet on bad signal reconnects a lot. Spending a
    // rotating refresh token on each one would be a burst of exchanges for no reason.
    expect(refresher.calls).toBe(0);
  });

  // ------------------------------------------------------------------ helpers

  async function connectedStream(): Promise<OrderStream> {
    const stream = TestBed.inject(OrderStream);
    signIn();
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(0);
    return stream;
  }

  function signIn(accessToken = freshToken): void {
    TestBed.inject(SessionStore).signIn({ accessToken, refreshToken: 'refresh-1' });
  }

  function change(status: OrderStatus, previousStatus: OrderStatus | null): OrderChanged {
    return {
      orderId: '11111111-1111-1111-1111-111111111111',
      orderNumber: 'FRIESLAB-260903-001',
      status,
      previousStatus,
      at: new Date().toISOString(),
    };
  }
});

/** A JWT with only the claim this file reads. The signature is never checked in a browser. */
function tokenExpiringIn(seconds: number): string {
  const payload = btoa(JSON.stringify({ sub: 'u1', exp: Math.floor(Date.now() / 1000) + seconds }))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');

  return `header.${payload}.signature`;
}

const freshToken = tokenExpiringIn(900);
const refreshedToken = tokenExpiringIn(900);
const expiredToken = tokenExpiringIn(-60);

/** Counts exchanges, because "did it refresh" is the whole question in two of these tests. */
class FakeRefresher {
  calls = 0;

  refresh(): Observable<string> {
    this.calls++;
    return of(refreshedToken);
  }
}

/** Enough of a HubConnection to drive every path this file has. */
class FakeHub {
  url = '';
  accessToken: (() => Promise<string>) | null = null;
  started = false;
  stopped = false;
  failNextStart = false;
  state: HubConnectionState = HubConnectionState.Disconnected;

  private handlers = new Map<string, (arg: OrderChanged) => void>();
  private reconnecting: (() => void)[] = [];
  private reconnected: (() => void)[] = [];
  private closed: (() => void)[] = [];

  on(name: string, handler: (arg: OrderChanged) => void): void {
    this.handlers.set(name, handler);
  }

  onreconnecting(handler: () => void): void {
    this.reconnecting.push(handler);
  }

  onreconnected(handler: () => void): void {
    this.reconnected.push(handler);
  }

  onclose(handler: () => void): void {
    this.closed.push(handler);
  }

  start(): Promise<void> {
    if (this.failNextStart) {
      this.failNextStart = false;
      return Promise.reject(new Error('the API is not answering'));
    }

    this.started = true;
    this.state = HubConnectionState.Connected;
    return Promise.resolve();
  }

  stop(): Promise<void> {
    this.stopped = true;
    this.state = HubConnectionState.Disconnected;
    return Promise.resolve();
  }

  push(change: OrderChanged): void {
    this.handlers.get('orderChanged')?.(change);
  }

  dropped(): void {
    this.state = HubConnectionState.Reconnecting;
    this.reconnecting.forEach((handler) => handler());
  }

  recovered(): void {
    this.state = HubConnectionState.Connected;
    this.reconnected.forEach((handler) => handler());
  }
}
