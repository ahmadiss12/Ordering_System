import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { App } from './app';

/**
 * The first screen, tested against a faked backend. It has no logic worth protecting yet — what
 * these pin is that the component actually renders what the API returns, and that a failing API
 * produces a message rather than an empty page with no explanation.
 */
describe('App', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('renders a card for each restaurant the API returns', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    http.expectOne('/api/restaurants').flush({
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
      totalCount: 2,
    });

    await fixture.whenStable();
    fixture.detectChanges();

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

    http.expectOne('/api/restaurants').error(new ProgressEvent('network error'));

    await fixture.whenStable();
    fixture.detectChanges();

    // A blank page leaves someone guessing whether the API is down or the data is empty.
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Could not reach the API');
  });
});
