import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { AuthService, Roles } from 'auth';
import { Shell } from './shell';

/**
 * The shell decides one thing worth testing: which sections a given account is offered.
 *
 * Getting it wrong is not a security hole — every endpoint behind these links is checked again
 * on the server — but it is the difference between an app that fits the person using it and one
 * that dangles a Settings link at a staff member so it can refuse them when they click it.
 */
describe('Shell', () => {
  function renderAs(...roles: string[]) {
    const held = signal(roles);

    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            session: {
              email: signal('someone@frieslab.test'),
              roles: held,
              restaurantId: signal('11111111-1111-1111-1111-111111111111'),
              hasAnyRole: (...wanted: string[]) => wanted.some((r) => held().includes(r)),
            },
            logout: () => Promise.resolve(),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    return fixture;
  }

  function sectionLabels(fixture: { nativeElement: HTMLElement }): string[] {
    return [...fixture.nativeElement.querySelectorAll('mat-nav-list a')].map((a) =>
      (a as HTMLElement).querySelector('[matListItemTitle]')!.textContent!.trim(),
    );
  }

  afterEach(() => TestBed.resetTestingModule());

  it('offers an owner every section', () => {
    const fixture = renderAs(Roles.RestaurantOwner);

    expect(sectionLabels(fixture)).toEqual(['Overview', 'Queue', 'Menu', 'Settings']);
  });

  it('hides owner-only sections from staff', () => {
    const fixture = renderAs(Roles.RestaurantStaff);

    // Staff work the queue and edit the menu, but do not change fees, zones or who else has
    // an account.
    expect(sectionLabels(fixture)).toEqual(['Overview', 'Queue', 'Menu']);
  });

  it('offers a platform admin only the sections that are not restaurant-scoped', () => {
    const fixture = renderAs(Roles.PlatformAdmin);

    // A platform admin holds neither restaurant role, so the restaurant sections are not theirs.
    expect(sectionLabels(fixture)).toEqual(['Overview']);
  });

  it('links each section to its own route', () => {
    const fixture = renderAs(Roles.RestaurantOwner);

    const hrefs = [...fixture.nativeElement.querySelectorAll('mat-nav-list a')].map((a) =>
      (a as HTMLAnchorElement).getAttribute('href'),
    );

    // '/' rather than '' for the landing section: an empty segment does not match the URL
    // exactly, and the active highlight silently never appears.
    expect(hrefs).toEqual(['/', '/orders', '/menu', '/settings']);
  });
});
