import { NAV, NAV_ITEMS, navChild } from './navigation';

/**
 * The invariant navigation.ts exists to hold: a section's roles decide both what appears in the
 * sidenav and what its route allows. If those ever came apart, the dashboard would offer links
 * that bounce to /forbidden — which users read as a broken app rather than a locked door.
 */
describe('navChild', () => {
  const load = () => Promise.resolve(class {});

  it('guards a route whose section declares roles', () => {
    const route = navChild(NAV.settings, load);

    expect(route.path).toBe('settings');
    expect(route.canActivate?.length).toBe(1);
  });

  it('leaves a section open when it declares no roles', () => {
    // Overview is for anyone signed in; authGuard on the shell above it is enough.
    const route = navChild(NAV.overview, load);

    expect(route.path).toBe('');
    expect(route.canActivate).toBeUndefined();
  });

  it('lists every declared section in the sidenav', () => {
    // Guards against a section being added to NAV and quietly never rendered.
    expect(NAV_ITEMS).toEqual(Object.values(NAV));
    expect(NAV_ITEMS.length).toBeGreaterThan(0);
  });

  it('gives every section a distinct path', () => {
    const paths = NAV_ITEMS.map((item) => item.path);

    expect(new Set(paths).size).toBe(paths.length);
  });
});
