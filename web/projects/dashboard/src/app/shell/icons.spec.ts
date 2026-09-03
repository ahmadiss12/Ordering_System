import bundledIcons from 'material-icons/_data/versions.json';

/**
 * Every icon this application names exists in the font it ships.
 *
 * This has bitten twice. Once when the icons were fetched from Google's CDN and a blocked request
 * left the ligature showing as the word `res` on screen; and again when a column header asked for
 * `skillet`, which exists in Material Symbols but not in the classic Material Icons set bundled
 * here — so the header simply had no icon and nothing anywhere said why.
 *
 * That is the shape of the bug worth a test: it is not a crash, not a console error and not a
 * failed request. It is a word, or a blank, rendered where a picture should be, and the only way
 * to notice is to look at the right screen on the right day.
 */
describe('icons', () => {
  // The package's own manifest, keyed by icon name. Read as data rather than parsed out of the
  // Sass map next to it, which the build compiles rather than handing back as text.
  const available = new Set(Object.keys(bundledIcons));

  const templates = import.meta.glob('../**/*.html', {
    query: '?raw',
    import: 'default',
    eager: true,
  });
  const sources = import.meta.glob('../**/*.ts', { query: '?raw', import: 'default', eager: true });

  it('reads the bundled font, so the list below means something', () => {
    // Without this the set could be empty and every assertion would pass vacuously.
    expect(available.size).toBeGreaterThan(1000);
    expect(available.has('restaurant_menu')).toBe(true);
  });

  it('names only icons the bundled font actually has', () => {
    const missing = [...used()].filter((name) => !available.has(name)).sort();

    expect(missing, `not in the bundled Material Icons font: ${missing.join(', ')}`).toEqual([]);
  });

  /**
   * Every ligature the application asks for.
   *
   * Templates give up `<mat-icon>name</mat-icon>` directly, and the quoted strings inside an
   * interpolated one — `{{ live ? 'wifi' : 'wifi_off' }}` is two icons, and picking only the
   * literal case would quietly skip exactly the ones somebody typed by hand.
   */
  function used(): Set<string> {
    const names = new Set<string>();

    for (const source of Object.values(templates) as string[]) {
      for (const [, body] of source.matchAll(/<mat-icon[^>]*>([\s\S]*?)<\/mat-icon>/g)) {
        if (body.includes('{{')) {
          for (const [, quoted] of body.matchAll(/'([a-z0-9_]+)'/g)) {
            names.add(quoted);
          }
        } else if (/^[a-z0-9_]+$/.test(body.trim())) {
          names.add(body.trim());
        }
      }
    }

    // Icons chosen in TypeScript rather than markup — the sidenav's sections and the board's
    // column headers both live there.
    for (const source of Object.values(sources) as string[]) {
      for (const [, name] of source.matchAll(/\bicon:\s*'([a-z0-9_]+)'/g)) {
        names.add(name);
      }
    }

    return names;
  }

  it('finds the icons it is supposed to be checking', () => {
    // The regexes above are the fragile part. If one stops matching, the test above passes on an
    // empty set and proves nothing — which is how it would fail silently rather than loudly.
    const names = used();

    expect(names.size).toBeGreaterThan(10);
    expect(names).toContain('restaurant_menu');
    expect(names).toContain('soup_kitchen');
    expect(names).toContain('wifi_off');
  });
});
