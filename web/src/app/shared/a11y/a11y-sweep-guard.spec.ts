/// <reference types="vite/client" />

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { CONTRAST_RULES, contrastRatio } from './contrast-rules';

/**
 * Phase 34a — <b>proving the accessibility sweep is complete, mechanically.</b>
 *
 * NFR-6.2 asks for WCAG 2.1 AA. Most of that standard is a judgement a person has to make; a
 * minority of it is decidable from a template's source text alone, with no browser and no human.
 * This file is that minority, and the split is the phase's Decision A: a check belongs here exactly
 * when it can be decided from the source without rendering the page and without anyone forming an
 * opinion. Everything else — focus order, reading order, the quality of an error message, whether a
 * label says the right thing — is in the status doc's human-pass list instead, because a guard that
 * asserts something it cannot actually decide is worse than no guard: it reports green.
 *
 * The sibling of phase-23's `sweep-guard.spec.ts` and phase-33's `navigation-catalog.spec.ts`, and
 * built to the same three rules that make a guard real rather than decorative:
 *
 *  * it asserts the glob matched a plausible number of templates first, so a broken glob cannot make
 *    every other assertion pass vacuously;
 *  * every allow-list entry states its reason, and is itself checked to still point at a file that
 *    exists, so a rename cannot quietly turn an exemption into a hole;
 *  * failures print the offending paths, because a count is not actionable.
 *
 * The failure this exists to prevent is not getting one wrong today. It is phase 35 adding the 349th
 * unlabelled input or the 162nd status badge, in an app where the rest of the screens are correct —
 * which is invisible to `ng build`, invisible to every component test, and invisible to anyone who
 * is not using a screen reader.
 */
const templates = import.meta.glob('/src/app/**/*.html', { query: '?raw', import: 'default', eager: true }) as Record<
  string,
  string
>;

const entries = Object.entries(templates);

/**
 * The global stylesheet, read off disk so the contrast rules can be checked against what ships.
 *
 * Not `import.meta.glob(..., '?raw')` like the templates above: Vite compiles `.scss`, and both
 * `?raw` and `?inline` hand back an **empty string** rather than an error -- so the glob "succeeds",
 * `toBeDefined()` passes, and every assertion over the contents is vacuously true. The first draft
 * of this file did exactly that, and the only reason it was caught is that the failure message
 * happened to print the length. `src/testing/node-shims.d.ts` supplies the two typings this needs.
 */
function readStylesheet(): string {
  return readFileSync(resolve(process.cwd(), 'src/styles.scss'), 'utf8');
}

/** Templates allowed to hold a form control with no accessible name, each with the reason. */
const UNNAMED_CONTROL_ALLOWED: ReadonlyMap<string, string> = new Map([
  [
    '/src/app/shared/formatting/bs-date-input.html',
    'This component IS a control. Its name comes from the caller, which passes `inputId` and points ' +
      'its own <label for> at the same id — asserted by the date-label test below, not assumed.',
  ],
]);

// ---------------------------------------------------------------------------------------------
// Source scanning. Deliberately regex over raw template text rather than a parsed DOM: an Angular
// template is not HTML (@if / @for / bindings), and every parser worth using would either choke on
// it or normalise away the very attributes being checked.
// ---------------------------------------------------------------------------------------------

const CONTROL = /<(input|select|textarea)\b((?:[^>"]|"[^"]*")*?)(\/?)>/gs;
const LABEL_FOR = /<label\b[^>]*?\bfor="([^"]+)"/g;
const LABEL_FOR_BOUND = /<label\b(?:[^>"]|"[^"]*")*?\[(?:attr\.)?for\]="([^"]+)"/g;
const LABEL_BOUNDARY = /<label\b|<\/label>/g;
const CLICKABLE = /<(button|a)\b((?:[^>"]|"[^"]*")*?)>(.*?)<\/\1>/gs;
const TH = /<th\b((?:[^>"]|"[^"]*")*?)>/g;
const ICON = /<i\s((?:[^>"]|"[^"]*")*?)\/?>/g;
const CLASS_ATTR = /class="([^"]*)"/g;

function matches(source: string, pattern: RegExp): RegExpExecArray[] {
  const re = new RegExp(pattern.source, pattern.flags.includes('g') ? pattern.flags : `${pattern.flags}g`);
  const found: RegExpExecArray[] = [];
  let m: RegExpExecArray | null;
  while ((m = re.exec(source)) !== null) {
    found.push(m);
  }
  return found;
}

/** True when `index` falls between a `<label>` and its `</label>` — implicit association. */
function insideLabel(source: string, index: number): boolean {
  let depth = 0;
  for (const m of matches(source.slice(0, index), LABEL_BOUNDARY)) {
    depth += m[0] === '<label' ? 1 : -1;
  }
  return depth > 0;
}

/** The visible text of a control, with tags and control-flow blocks removed. */
function visibleText(inner: string): string {
  return inner
    .replace(/<[^>]+>/g, '')
    .replace(/@[a-z]+\s*\([^)]*\)\s*\{|\}\s*@else\s*\{|\}/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

function report(found: readonly string[], advice: string): string {
  return `${advice}\n  ${found.join('\n  ')}`;
}

describe('Phase 34a accessibility sweep', () => {
  it('finds the templates to scan at all (guards against a glob that silently matches nothing)', () => {
    // Without this every assertion below would pass vacuously — the classic way a guard stops
    // guarding. 161 templates at the time of writing.
    expect(entries.length).toBeGreaterThan(140);
  });

  it('gives every form control an accessible name (WCAG 1.3.1, 3.3.2, 4.1.2)', () => {
    const found: string[] = [];

    for (const [path, source] of entries) {
      if (UNNAMED_CONTROL_ALLOWED.has(path)) {
        continue;
      }

      const ids = new Set(matches(source, LABEL_FOR).map((m) => m[1]));
      const bound = new Set(matches(source, LABEL_FOR_BOUND).map((m) => m[1].trim()));

      for (const m of matches(source, CONTROL)) {
        const [tag, attrs] = [m[1], m[2]];
        if (tag === 'input' && /type="hidden"/.test(attrs)) {
          continue;
        }

        const id = /\bid="([^"]+)"/.exec(attrs)?.[1];
        const boundId = /\[id\]="([^"]+)"/.exec(attrs)?.[1]?.trim();

        const named =
          attrs.includes('aria-label') ||
          attrs.includes('aria-labelledby') ||
          (id !== undefined && ids.has(id)) ||
          (boundId !== undefined && bound.has(boundId)) ||
          insideLabel(source, m.index);

        if (!named) {
          found.push(`${path}  ${m[0].replace(/\s+/g, ' ').slice(0, 80)}`);
        }
      }
    }

    expect(
      found,
      report(
        found,
        'Every input/select/textarea needs an accessible name: a <label for> pointing at its id, a ' +
          'wrapping <label>, or an aria-label when there is no visible label to point at.',
      ),
    ).toEqual([]);
  });

  it('associates every <label> with something, or has no label to associate (WCAG 1.3.1)', () => {
    // The mirror of the test above, and it catches what that one cannot: a *component*-wrapped
    // control. 121 date fields had a visible label and a real input and nothing joining them,
    // because <app-bs-date-input> is not an <input> and no scan for controls could see it.
    const found: string[] = [];

    for (const [path, source] of entries) {
      for (const m of matches(source, /<label\b((?:[^>"]|"[^"]*")*?)>((?:(?!<\/label>).)*?)<\/label>/gs)) {
        const [attrs, body] = [m[1], m[2]];
        // Three spellings, and the third is easy to miss: `[attr.for]="…"` contains no `for=`
        // substring at all, so a naive check reports every one of them as an orphan.
        if (attrs.includes('for=') || attrs.includes('[for]') || attrs.includes('[attr.for]')) {
          continue;
        }
        // A label wrapping its own control needs no `for`.
        if (/<(input|select|textarea)\b/.test(body)) {
          continue;
        }
        found.push(`${path}  <label>${visibleText(body).slice(0, 40)}</label>`);
      }
    }

    expect(
      found,
      report(found, 'A <label> must either point at a control with `for`, or wrap the control it names.'),
    ).toEqual([]);
  });

  it('gives every icon-only button and link an accessible name (WCAG 4.1.2)', () => {
    const found: string[] = [];

    for (const [path, source] of entries) {
      for (const m of matches(source, CLICKABLE)) {
        const [attrs, inner] = [m[2], m[3]];
        if (visibleText(inner) || !/<(i|svg|img)\b/.test(inner)) {
          continue;
        }
        if (attrs.includes('aria-label') || attrs.includes('title=')) {
          continue;
        }
        found.push(`${path}  ${m[0].replace(/\s+/g, ' ').slice(0, 90)}`);
      }
    }

    expect(
      found,
      report(
        found,
        'A control whose only content is an icon has no accessible name at all — every icon in this ' +
          'app is aria-hidden, so a screen reader announces "button" and nothing else. Add aria-label.',
      ),
    ).toEqual([]);
  });

  it('hides every decorative icon from assistive technology (WCAG 1.1.1)', () => {
    const found: string[] = [];

    for (const [path, source] of entries) {
      for (const m of matches(source, ICON)) {
        if (!m[1].includes('aria-hidden')) {
          found.push(`${path}  <i ${m[1].replace(/\s+/g, ' ').slice(0, 60)}>`);
        }
      }
    }

    expect(
      found,
      report(
        found,
        'A Bootstrap Icons glyph is CSS pseudo-content with no text alternative, so a screen reader ' +
          'reads a private-use codepoint or nothing. Every <i> in this app is decorative: mark it ' +
          'aria-hidden="true" and put the meaning on the control instead.',
      ),
    ).toEqual([]);
  });

  it('declares what every table header heads (WCAG 1.3.1)', () => {
    const found: string[] = [];

    for (const [path, source] of entries) {
      for (const m of matches(source, TH)) {
        if (!m[1].includes('scope=')) {
          found.push(`${path}  <th ${m[1].replace(/\s+/g, ' ').slice(0, 50)}>`);
        }
      }
    }

    expect(
      found,
      report(found, 'Add scope="col" to a column header, or scope="colgroup" when it spans several.'),
    ).toEqual([]);
  });

  it('never pairs a subtle background with its own non-emphasis text colour (WCAG 1.4.3)', () => {
    // This is the check that would have been wrong to write as a hand-maintained list of forbidden
    // class pairs, because the reason a pair is forbidden is a *number*: `text-success` on
    // `bg-success-subtle` is 3.49:1 against a 4.5:1 requirement. `contrast-rules.ts` computes the
    // ratios from the palette, and the rule below is derived from that computation — see
    // contrast-rules.spec.ts, which pins the arithmetic itself.
    const forbidden = CONTRAST_RULES.filter((r) => !r.passesAA);
    const found: string[] = [];

    for (const [path, source] of entries) {
      for (const m of matches(source, CLASS_ATTR)) {
        const classes = m[1];
        for (const rule of forbidden) {
          if (rule.background && !classes.includes(rule.background)) {
            continue;
          }
          if (new RegExp(`\\b${rule.foreground}\\b(?!-)`).test(classes)) {
            found.push(`${path}  ${rule.foreground}${rule.background ? ` on ${rule.background}` : ''} — ${rule.ratio.toFixed(2)}:1`);
          }
        }
      }
    }

    expect(
      found,
      report(
        found,
        'This colour pair does not reach 4.5:1. Use the -emphasis variant of the same tone: it is ' +
          "Bootstrap's own intended partner for a -subtle background and clears 7:1.",
      ),
    ).toEqual([]);
  });

  it('keeps every allow-list entry pointing at a template that still exists', () => {
    for (const path of UNNAMED_CONTROL_ALLOWED.keys()) {
      expect(templates[path], `allow-listed template no longer exists: ${path}`).toBeDefined();
    }
  });

  it('passes every caller of the date component an inputId, since that is what the allow-list assumes', () => {
    // The exemption above is only honest while this holds. If a later phase adds a date field and
    // forgets `inputId`, the exemption would be covering a real unlabelled control.
    const found: string[] = [];

    for (const [path, source] of entries) {
      if (path.endsWith('bs-date-input.html')) {
        continue;
      }
      for (const m of matches(source, /<app-bs-date-input\b((?:[^>"]|"[^"]*")*?)\/?>/gs)) {
        if (!m[1].includes('[inputId]')) {
          found.push(`${path}  <app-bs-date-input ${m[1].replace(/\s+/g, ' ').slice(0, 50)}>`);
        }
      }
    }

    expect(
      found,
      report(found, 'Pass [inputId] and point the field\'s <label for> at the same id.'),
    ).toEqual([]);
  });
});

describe('the contrast arithmetic the sweep is derived from', () => {
  it('agrees with the WCAG worked examples', () => {
    // Sanity anchors from the specification itself, so a bug in the relative-luminance formula
    // cannot make the whole contrast rule silently permissive.
    expect(contrastRatio('#ffffff', '#000000')).toBeCloseTo(21, 5);
    expect(contrastRatio('#ffffff', '#ffffff')).toBeCloseTo(1, 5);
    expect(contrastRatio('#777777', '#ffffff')).toBeCloseTo(4.48, 2);
  });

  it('shows why Bootstrap\'s stock tones had to be darkened', () => {
    // Bootstrap's defaults are tuned to clear 4.5:1 against **pure white**, and only just: 4.50:1
    // for $primary. This app's page background is #f8f9fa, and against that ground the same colours
    // fall below the line. The margin was never there; it was the white in the worked example.
    for (const [name, hex] of [
      ['primary', '#0d6efd'],
      ['secondary', '#6c757d'],
      ['success', '#198754'],
      ['danger', '#dc3545'],
    ] as const) {
      expect(contrastRatio(hex, '#ffffff'), `${name} on a white card`).toBeGreaterThanOrEqual(4.5);
      expect(contrastRatio(hex, '#f8f9fa'), `${name} on the page body`).toBeLessThan(4.5);
    }
  });

  it('measures every pairing the app can produce as reaching AA, bar the two that must use -emphasis', () => {
    const failing = CONTRAST_RULES.filter((r) => !r.passesAA).map(
      (r) => `${r.foreground} on ${r.background ?? 'the page'} — ${r.ratio.toFixed(2)}:1`,
    );

    // `text-warning` (#ffc107) and `text-info` (#0dcaf0) are yellow and cyan: no darkening keeps
    // them recognisable as those tones, so unlike the other four they are not re-pointed — their
    // `-emphasis` variants are the only conformant spelling, and the sweep guard forbids the plain
    // ones outright. Everything else in the palette clears 4.5:1 on every ground it is used on.
    expect(failing.sort()).toEqual(
      [
        'text-info on bg-info-subtle — 1.68:1',
        'text-info on bg-info bg-opacity-10 — 1.82:1',
        'text-info on the page — 1.86:1',
        'text-warning on bg-warning-subtle — 1.47:1',
        'text-warning on bg-warning bg-opacity-10 — 1.55:1',
        'text-warning on the page — 1.55:1',
      ].sort(),
    );
  });

  it('keeps the shipped stylesheet in step with the palette these rules are measured from', () => {
    // Without this the guard measures a palette the app might no longer ship: `contrast-rules.ts`
    // would go on reporting 6.11:1 for a `text-primary` that had quietly reverted to Bootstrap's
    // 4.27:1 default, and every assertion above would stay green while the app failed AA.
    const stylesheet = readStylesheet();

    // Non-empty, not merely defined: an empty string would make every check below pass vacuously,
    // which is how the first draft of this test managed to guard nothing at all.
    expect(stylesheet.length, 'src/styles.scss read back empty').toBeGreaterThan(500);

    for (const [utility, hex] of [
      ['text-primary', '#0a58ca'],
      ['text-secondary', '#565e64'],
      ['text-success', '#146c43'],
      ['text-danger', '#b02a37'],
    ] as const) {
      const rule = new RegExp(`\\.${utility}\\s*\\{[\\s\\S]*?color:\\s*${hex}`, 'i');
      expect(
        rule.test(stylesheet),
        `styles.scss no longer paints .${utility} ${hex}. It contains ${hex}: ${stylesheet.includes(hex)}; ` +
          `it contains .${utility}: ${stylesheet.includes(`.${utility}`)}; length ${stylesheet.length}`,
      ).toBe(true);
    }
  });
});
