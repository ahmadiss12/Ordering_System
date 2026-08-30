import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideApiClient } from 'api-client';
import { provideAuth } from 'auth';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideAnimationsAsync(),
    // Provides the HTTP client together with the auth interceptor, so the two cannot be
    // registered apart and silently lose token attachment.
    provideAuth(),
    // Empty base URL means relative requests, which the dev-server proxy forwards to the API.
    provideApiClient(),
  ],
};
