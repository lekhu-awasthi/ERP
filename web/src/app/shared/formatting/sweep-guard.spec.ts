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

/**
 * Phase 48 -- the `.ts` files too, for the same reason phase 40 widened `a11y-sweep-guard`'s glob: a
 * predicate naming a **file extension** silently stops covering components that declare an inline
 * `template:`. Widening it here found nothing wrong, which is the honest result and is not the same
 * as never having looked.
 */
const inlineTemplates = import.meta.glob('/src/app/**/*.ts', { query: '?raw', import: 'default', eager: true }) as Record<
  string,
  string
>;

/** Every template source the date checks scan: standalone `.html` plus inline `template:` literals. */
const allSources: ReadonlyArray<readonly [string, string]> = [
  ...Object.entries(templates),
  ...Object.entries(inlineTemplates).filter(([path]) => !path.endsWith('.spec.ts')),
];

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

/**
 * Phase 48 -- <b>every date a user reads goes through `NepaliDatePipe`, including an instant.</b>
 *
 * <p>Phase 23 swept native date <i>inputs</i> and inline `.toFixed(2)`, and this guard has policed
 * both since. It never asked the mirror question about date <i>output</i>, and the answer was 23
 * uses of Angular's own `DatePipe` across 19 templates plus four raw ISO strings in
 * `activity-panel.html`. `| date:` renders a Gregorian date in the browser's locale, so those
 * screens showed AD dates to a user who had set the calendar to BS -- the exact failure NFR-1.1
 * exists to prevent, arriving through the half nobody had swept.</p>
 *
 * <p>The two checks are deliberately different in kind. The first is a ban on a pipe, which is
 * exact. The second is a ban on rendering a known instant-bearing field without a pipe, which is
 * heuristic -- it can only recognise field names it knows about, and that is stated here rather
 * than implied, so nobody reads a pass as proof that no raw timestamp exists anywhere.</p>
 */
describe('Phase 48 -- dates a user reads are rendered in the tenant calendar', () => {
  it('finds both kinds of template source (guards against a glob that matches nothing)', () => {
    expect(Object.keys(templates).length).toBeGreaterThan(80);
    expect(Object.keys(inlineTemplates).length).toBeGreaterThan(80);
    expect(allSources.length).toBeGreaterThan(160);
  });

  it("never renders a date with Angular's DatePipe, which ignores the BS/AD toggle", () => {
    const found = allSources
      .filter(([, source]) => /\|\s*date\s*:/.test(source))
      .map(([path]) => path)
      .sort();

    expect(
      found,
      "Use `| nepaliDate` (optionally `: 'datetime'` or `: 'datetime-seconds'`) instead of Angular's " +
        `DatePipe, which renders Gregorian dates in the browser locale:\n  ${found.join('\n  ')}`,
    ).toEqual([]);
  });

  it('never interpolates a timestamp field without a pipe', () => {
    // Heuristic by design: these are the DateTimeOffset-valued DTO field names this app uses. A
    // field named something else is invisible to this check, which is why the check is a floor and
    // not a proof.
    const instantFields = /\{\{\s*[\w.?()]*\b(createdAt|sentAt|approvedAt|uploadedAt|occurredAt|linkedAt|extractionAttemptedAt)\b\s*\}\}/;

    const found = allSources
      .filter(([, source]) => instantFields.test(source))
      .map(([path]) => path)
      .sort();

    expect(
      found,
      'A timestamp rendered raw prints an ISO string like 2026-09-14T14:00:21.099+00:00. Pipe it ' +
        `through \`| nepaliDate: 'datetime-seconds'\`:\n  ${found.join('\n  ')}`,
    ).toEqual([]);
  });

  it("renders the activity feed's own four timestamps through the pipe, with seconds", () => {
    // Named explicitly because this component is the phase's subject and has 17 hosts: a regression
    // here is 17 screens, and the checks above would still pass if someone swapped the pipe for a
    // hand-rolled string.
    const panel = templates['/src/app/features/contacts/activity-panel/activity-panel.html'];
    expect(panel, 'activity-panel.html not found').toBeTruthy();
    expect(panel.length).toBeGreaterThan(100);

    const piped = panel.match(/\|\s*nepaliDate:\s*'datetime-seconds'/g) ?? [];
    expect(
      piped.length,
      'the comment, activity, SMS and email-log timestamps must each go through the pipe',
    ).toBe(4);
  });
});

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
