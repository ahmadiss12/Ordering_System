import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideApiClient } from 'api-client';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    // withFetch: the fetch backend, which is what the interceptor in step 6 will hook.
    provideHttpClient(withFetch()),
    provideAnimationsAsync(),
    // Empty base URL means relative requests, which the dev-server proxy forwards to the API.
    provideApiClient(),
  ],
};
