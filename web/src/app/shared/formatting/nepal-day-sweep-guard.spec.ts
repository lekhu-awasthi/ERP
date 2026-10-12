/**
 * Phase 89 -- a calendar day on this client is the **Nepal** day (`nepal-time.ts`), never the UTC day or the browser's.
 *
 * <p>Before this phase, 67 sources took "today" as `new Date().toISOString().slice(0, 10)`, which is yesterday from
 * 00:00 to 05:45 in Kathmandu, and 18 report pages took the first of the month as
 * `new Date(y, m, 1).toISOString()`: local midnight on the 1st, printed in UTC, which is the last day of the month
 * before. Every report opened a day early, and the invoice and purchase-bill forms suggested every due date a day early
 * the same way (a local-midnight parse printed in UTC). `nepalToday`, `nepalFirstOfMonth`, `nepalDayOf` and `shiftDay`
 * replaced them all; this keeps the shapes out.</p>
 */
const sources = import.meta.glob('/src/app/**/*.ts', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const SHAPES: readonly { readonly name: string; readonly pattern: RegExp }[] = [
  // The UTC date of now. (An instant kept whole, `new Date().toISOString()`, is not a day and is rendered through
  // NepaliDatePipe's datetime mode, so only the cut to ten characters is banned.)
  { name: 'UTC today', pattern: /new Date\(\)\.toISOString\(\)\.(?:slice|substring)\(0, ?10\)/ },
  // A date built from the browser's calendar fields, then printed in UTC.
  { name: 'local calendar fields printed in UTC', pattern: /new Date\((?:Date\.UTC\()?\w+\.getFullYear\(\)[^;]*\.toISOString\(\)/ },
  // A day parsed at local midnight (no `Z`), which is the day before in UTC east of Greenwich.
  { name: 'local-midnight parse', pattern: /T00:00:00`\)/ },
  // Day arithmetic on the browser's calendar.
  { name: 'local day arithmetic', pattern: /\.setDate\(/ },
];

function guarded(): [string, string][] {
  return Object.entries(sources).filter(
    ([path]) => !path.endsWith('.spec.ts') && !path.endsWith('/shared/formatting/nepal-time.ts'),
  );
}

describe('the Nepal-day sweep', () => {
  it('reads every source it guards', () => {
    // Phase 34a: assert the input is non-empty, not merely defined.
    expect(guarded().length).toBeGreaterThan(300);
    expect(guarded().every(([, source]) => typeof source === 'string' && source.length > 0)).toBe(true);
  });

  for (const shape of SHAPES) {
    it(`has no ${shape.name} outside nepal-time.ts`, () => {
      const offenders = guarded()
        .filter(([, source]) => shape.pattern.test(source))
        .map(([path]) => path);

      expect(offenders).toEqual([]);
    });
  }

  it('recognises each shape it bans', () => {
    // A pattern that matches nothing passes vacuously, so each is held to the code it replaced.
    const replaced = [
      'return new Date().toISOString().slice(0, 10);',
      'return new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);',
      'const from = new Date(`${this.date()}T00:00:00`);',
      'next.setDate(next.getDate() + 1);',
    ];
    SHAPES.forEach((shape, i) => expect(shape.pattern.test(replaced[i])).toBe(true));
    expect(
      SHAPES[1].pattern.test('return new Date(Date.UTC(now.getFullYear(), now.getMonth(), 1)).toISOString().slice(0, 10);'),
    ).toBe(true);
  });
});
