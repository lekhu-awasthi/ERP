/**
 * Phases 5–7's select race, guarded at last. A native `<select>` whose value comes from a signal must bind `[selected]`
 * on each `<option>`, never `[value]` on the select itself: the options are created after the select's value binding has
 * run, so the browser shows its first option (or the placeholder) while the signal holds another. Phases 5–7 found it
 * persisting wrong warehouses; phase 93's browser pass found it showing the walk-in on every converted credit note while
 * SQL held the real customer, and a census found four more selects (Cash Transfer, Opening Balances) of the same shape.
 *
 * <p>So the rule is asserted over every template and inline `template:`: no `<select>` opening tag carries a bare
 * `[value]` binding. Comments are stripped first, because a template explaining the rule names the shape it forbids
 * (phase 47's lesson about source guards).</p>
 */
const templates = import.meta.glob('/src/app/**/*.html', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const sources = import.meta.glob(['/src/app/**/*.ts', '!/src/app/**/*.spec.ts'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const HTML_COMMENT = /<!--[\s\S]*?-->/g;

/** A `<select>` opening tag carrying `[value]=`, not `[attr.value]` or an option's own `[value]`. */
const SELECT_WITH_VALUE = /<select\b[^>]*\s\[value\]=/g;

/** The markup a source can hold: its inline `template:` literal, if it has one. */
function inlineTemplate(source: string): string {
  const match = /\btemplate:\s*`([\s\S]*?)`/.exec(source);
  return match ? match[1] : '';
}

function offenders(markup: Record<string, string>): string[] {
  return Object.entries(markup).flatMap(([path, text]) =>
    [...text.replace(HTML_COMMENT, '').matchAll(SELECT_WITH_VALUE)].map((m) => `${path}: ${m[0].slice(0, 80)}`),
  );
}

describe('the select-binding sweep', () => {
  it('reads every template it guards, comments removed', () => {
    // Phase 34a: assert the input is non-empty, not merely defined.
    expect(Object.keys(templates).length).toBeGreaterThan(100);
    expect(Object.values(sources).some((x) => inlineTemplate(x).includes('<select'))).toBe(true);
    expect('<p><!-- a <select [value]="x"> named in prose --></p>'.replace(HTML_COMMENT, '')).toBe('<p></p>');
  });

  it('never binds [value] on a native select', () => {
    const inline = Object.fromEntries(Object.entries(sources).map(([path, source]) => [path, inlineTemplate(source)]));

    expect([...offenders(templates), ...offenders(inline)]).toEqual([]);
  });

  it('recognises the shape it forbids', () => {
    // The pattern must catch the binding wherever it sits in a multi-line opening tag, and leave an option's alone.
    expect(offenders({ 'x.html': '<select id="a"\n  class="form-select"\n  [value]="pick()"\n>' })).toHaveLength(1);
    expect(offenders({ 'x.html': '<select (change)="f($event)"><option [value]="a.id" [selected]="ok">' })).toEqual([]);
  });
});
