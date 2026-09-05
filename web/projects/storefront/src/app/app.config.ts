import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideApiClient } from 'api-client';
import { provideAuth } from 'auth';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withComponentInputBinding(),
      // Opening a restaurant should start at the top of its menu, and going back to the list
      // should return to where it was left. Neither is the router's default.
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' }),
    ),
    provideAnimationsAsync(),
    // Provides the HTTP client together with the auth interceptor, so the two cannot be
    // registered apart and silently lose token attachment. Nothing here is signed in yet; the
    // interceptor simply has no token to attach until Step 2.
    provideAuth(),
    // Empty base URL means relative requests, which the dev-server proxy forwards to the API.
    provideApiClient(),
  ],
};
