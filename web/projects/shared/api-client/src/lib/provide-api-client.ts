import { EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import { API_BASE_URL } from './api-client';

/**
 * Supplies the base URL every generated service resolves against.
 *
 * The default is an empty string, which means relative URLs — exactly right in development,
 * where the Angular dev server proxies `/api` to the backend so the browser only ever sees one
 * origin and CORS never enters the picture. A deployed build passes its own origin instead.
 */
export function provideApiClient(baseUrl = ''): EnvironmentProviders {
  return makeEnvironmentProviders([{ provide: API_BASE_URL, useValue: baseUrl }]);
}
