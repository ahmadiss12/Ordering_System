import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideApiClient } from 'api-client';
import { App } from './app';

/**
 * The first screen, tested against a faked backend.
 *
 * The generated client asks for `responseType: 'blob'` so it can read an error body before
 * deciding how to surface it, so these flush a Blob rather than a plain object. Matching how
 * the client actually calls is the point — a test that flushes something the real code would
 * never receive proves nothing.
 */
describe('App', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideApiClient(),
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('renders a card for each restaurant the API returns', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    respondWith({
      items: [
        {
          id: '1', name: 'FriesLab', slug: 'frieslab', description: 'Burgers',
          minOrderUsd: 8, defaultPrepMinutes: 20, isAcceptingOrders: true, isOpenNow: true,
        },
        {
          id: '2', name: 'Shawarma Station', slug: 'shawarma-station', description: 'Wraps',
          minOrderUsd: 6, defaultPrepMinutes: 15, isAcceptingOrders: true, isOpenNow: false,
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
    });

    await settled(fixture);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('FriesLab');
    expect(text).toContain('Shawarma Station');

    // The open/closed state is the one piece of server-computed truth on this screen.
    expect(text).toContain('Open now');
    expect(text).toContain('Closed');
  });

  it('explains itself when the API cannot be reached', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    request().error(new ProgressEvent('network error'));

    await settled(fixture);

    // A blank page leaves someone guessing whether the API is down or the data is empty.
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Could not reach the API');
  });

  /**
   * Waits until the component has actually finished loading, rather than guessing a delay.
   *
   * whenStable settles microtasks, but the generated client decodes its Blob response with a
   * FileReader, which resolves across several macrotask turns under jsdom. Polling for the
   * outcome is what makes this reliable instead of flaky on a slower machine.
   */
  async function settled(fixture: ComponentFixture<App>): Promise<void> {
    for (let attempt = 0; attempt < 50; attempt++) {
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve, 5));
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      const stillLoading = !text.includes('Min $') && !text.includes('Could not reach');
      if (!stillLoading) {
        return;
      }
    }
  }

  function request() {
    return http.expectOne((r) => r.url.startsWith('/api/restaurants'));
  }

  function respondWith(body: unknown): void {
    request().flush(new Blob([JSON.stringify(body)], { type: 'application/json' }));
  }
});
