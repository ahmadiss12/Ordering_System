import { EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './auth.interceptor';

/**
 * Wires the HTTP client together with the auth interceptor.
 *
 * Provided as one call so an app cannot register the client without the interceptor. Splitting
 * them would make it possible to add an HTTP feature later and silently lose token attachment
 * and the retry on expiry.
 */
export function provideAuth(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
  ]);
}
