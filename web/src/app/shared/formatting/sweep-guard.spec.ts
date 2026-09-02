/// <reference types="vite/client" />

/**
 * Phase 23, <b>Decision D — proving the two sweeps are complete, mechanically.</b>
 *
 * NFR-1.1 and NFR-1.2 were each a sweep across the whole app: 324 inline `.toFixed(2)` money
 * renders across 40 templates, and 66 native `<input type="date">` across 42. The failure mode that
 * matters is not getting one wrong today -- it is Phase 24 adding the 325th `.toFixed(2)` or the
 * 67th raw date input, leaving an app where some money is grouped and some is not and some dates
 * are BS and some are AD. A user cannot tell which is which, and a mis-read date in an accounting
 * system is a filed tax return with wrong numbers in it.
 *
 * A paragraph of intent in a status doc does not survive that. This does: it reads every template
 * in the app off disk at test time and fails the build on a new occurrence. If a screen ever has a
 * legitimate reason to opt out, add it to the allow-list below <i>with the reason</i> -- which
 * makes the exception a deliberate, reviewed act rather than a silent drift.
 */
const templates = import.meta.glob('/src/app/**/*.html', { query: '?raw', import: 'default', eager: true }) as Record<
  string,
  string
>;

/** Templates allowed to keep a raw `<input type="date">`, each with the reason it is exempt. */
const RAW_DATE_INPUT_ALLOWED: ReadonlyMap<string, string> = new Map([
  [
    '/src/app/shared/formatting/bs-date-input.html',
    'This component IS the replacement -- it wraps the one remaining native date input.',
  ],
]);

/** Templates allowed to keep an inline `.toFixed(2)`, each with the reason it is exempt. */
const INLINE_MONEY_ALLOWED: ReadonlyMap<string, string> = new Map();

function offenders(pattern: RegExp, allowed: ReadonlyMap<string, string>): string[] {
  return Object.entries(templates)
    .filter(([path]) => !allowed.has(path))
    .filter(([, source]) => pattern.test(source))
    .map(([path]) => path)
    .sort();
}

describe('Phase 23 sweep completeness', () => {
  it('finds the templates to scan at all (guards against a glob that silently matches nothing)', () => {
    // Without this, a broken glob would make every assertion below pass vacuously -- which is the
    // classic way a guard test stops guarding anything.
    expect(Object.keys(templates).length).toBeGreaterThan(80);
  });

  it('has no inline .toFixed(2) money formatting left in any template (NFR-1.2)', () => {
    const found = offenders(/\.toFixed\(2\)/, INLINE_MONEY_ALLOWED);
    expect(
      found,
      `Format money with the shared 'amount' pipe instead of .toFixed(2):\n  ${found.join('\n  ')}`,
    ).toEqual([]);
  });

  it('has no raw <input type="date"> left in any template (NFR-1.1)', () => {
    const found = offenders(/<input\b[^>]*type="date"/, RAW_DATE_INPUT_ALLOWED);
    expect(
      found,
      `Use <app-bs-date-input> instead of a native date input:\n  ${found.join('\n  ')}`,
    ).toEqual([]);
  });

  it('keeps every allow-list entry pointing at a template that still exists', () => {
    // An allow-list entry whose file has been renamed silently stops exempting anything -- or worse,
    // hides that the exemption is no longer needed.
    for (const path of [...RAW_DATE_INPUT_ALLOWED.keys(), ...INLINE_MONEY_ALLOWED.keys()]) {
      expect(templates[path], `allow-listed template no longer exists: ${path}`).toBeDefined();
    }
  });
});
