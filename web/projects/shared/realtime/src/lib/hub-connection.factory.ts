import { InjectionToken } from '@angular/core';
import { HubConnection, HubConnectionBuilder, IRetryPolicy } from '@microsoft/signalr';

/**
 * How long to wait before each reconnection attempt, in milliseconds.
 *
 * Fast at first — most drops are a tunnel or a lift and are over in seconds — then settling at
 * thirty. It never stops trying, which is the important part and is not SignalR's default: the
 * built-in policy gives up after about thirty seconds, and a tablet propped up in a kitchen that
 * quietly stopped reconnecting after half a minute of no signal is exactly the screen this whole
 * step exists to prevent.
 */
const RETRY_DELAYS_MS = [0, 1_000, 2_000, 5_000, 10_000, 30_000];

const forever: IRetryPolicy = {
  nextRetryDelayInMilliseconds: (context) =>
    RETRY_DELAYS_MS[Math.min(context.previousRetryCount, RETRY_DELAYS_MS.length - 1)],
};

/**
 * Builds the connection to a hub. Injected rather than constructed inline so a test can hand
 * {@link OrderStream} a stub and drive reconnects and messages without a server.
 */
export type HubConnectionFactory = (
  url: string,
  accessToken: () => Promise<string>,
) => HubConnection;

export const HUB_CONNECTION_FACTORY = new InjectionToken<HubConnectionFactory>(
  'HUB_CONNECTION_FACTORY',
  {
    providedIn: 'root',
    factory: () => defaultHubConnectionFactory,
  },
);

export const defaultHubConnectionFactory: HubConnectionFactory = (url, accessToken) =>
  new HubConnectionBuilder()
    .withUrl(url, {
      // Called on every connect and every reconnect, so a token that expired while the tablet
      // was asleep is replaced rather than replayed. A WebSocket handshake gets one attempt —
      // there is no 401-then-retry the way there is for an HTTP request.
      accessTokenFactory: accessToken,
    })
    .withAutomaticReconnect(forever)
    .build();
