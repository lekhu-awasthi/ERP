/// <reference types="vite/client" />

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { AA_NON_TEXT, CONTRAST_RULES, FOCUS_RING, FOCUS_RING_RULES, contrastRatio } from './contrast-rules';

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

/**
 * Phase 40 — <b>the components whose template is not a file.</b>
 *
 * Phase 39 found `SearchSweepGuardTests` blind to the two list queries that predate the type its
 * predicate names. This guard had the same shape of hole one layer down: its predicate names a file
 * <i>extension</i>, so five components with an inline `template:` string — the billing-location
 * picker, the two location filters, the location badge, the AD/BS calendar toggle — were outside
 * every assertion in this file and always had been. Widening it found nothing wrong, which is worth
 * saying: the hole was real and it happened to be empty, and the only way to know which was to look.
 *
 * A template literal is extracted rather than parsed, because the assertions below are regexes over
 * raw template text anyway. `TEMPLATE_LITERAL` stops at the first backtick, so a component whose
 * template contains one (none do, and a Bootstrap class name cannot) would be silently truncated —
 * hence the length assertion in the first test.
 */
const inlineTemplates = import.meta.glob('/src/app/**/*.ts', { query: '?raw', import: 'default', eager: true }) as Record<
  string,
  string
>;

const TEMPLATE_LITERAL = /^\s*template:\s*`([^`]*)`/m;

const inlineEntries = Object.entries(inlineTemplates)
  .filter(([path]) => !path.endsWith('.spec.ts'))
  .map(([path, source]) => [path, TEMPLATE_LITERAL.exec(source)?.[1] ?? ''] as const)
  .filter(([, template]) => template.trim().length > 0);

/**
 * Phase 47 — <b>an HTML comment is prose, not markup, and these assertions are regexes.</b>
 *
 * Every check in this file scans raw template text, so a comment that happens to *mention* a tag is
 * indistinguishable from one. The nesting check added below reported five templates on its first
 * run, and all five were comments explaining the very rule being checked — including this phase's
 * own "a &lt;select&gt; nested in an &lt;a&gt; is invalid HTML". Phase 40 met the mirror of this (a
 * backtick inside a comment inside an inline `template:` terminating the literal).
 *
 * Stripping comments can only remove false positives: markup that is commented out does not render,
 * so it cannot be an accessibility defect either. The offsets shift with the text, which is why it
 * happens once, here, rather than per assertion.
 */
const HTML_COMMENT = /<!--[\s\S]*?-->/g;

const entries: readonly (readonly [string, string])[] = [
  ...Object.entries(templates),
  ...inlineEntries,
].map(([path, source]) => [path, source.replace(HTML_COMMENT, '')] as const);

/**
 * Phase 47 — <b>the selectors of every component whose own template renders an interactive
 * control</b>, derived from the source rather than listed.
 *
 * The nesting check below asks whether a link contains a control. A regex for `<select>` inside
 * `<a>` would have found nothing on the four grids that actually shipped the defect, because what
 * they nest is `<app-custom-status-picker>` — a component. Listing the component selectors by hand
 * would make this phase's own list the wrong list the moment a sixteenth control component is
 * written (phase 30's lesson: find the rule, not the sample). So each `.ts` is read for its
 * `selector` and its template, and a selector joins this set when that template renders a control.
 */
const CONTROL_COMPONENTS: ReadonlySet<string> = (() => {
  const selectors = new Set<string>();
  const RENDERS_CONTROL = /<(input|select|textarea|button)\b/;

  for (const [path, source] of Object.entries(inlineTemplates)) {
    if (path.endsWith('.spec.ts')) {
      continue;
    }

    const selector = /selector:\s*'([^']+)'/.exec(source)?.[1];

    if (!selector) {
      continue;
    }

    const templateUrl = /templateUrl:\s*'\.\/([^']+)'/.exec(source)?.[1];
    const template = templateUrl
      ? (templates[`${path.slice(0, path.lastIndexOf('/'))}/${templateUrl}`] ?? '')
      : (TEMPLATE_LITERAL.exec(source)?.[1] ?? '');

    if (RENDERS_CONTROL.test(template.replace(HTML_COMMENT, ''))) {
      selectors.add(selector);
    }
  }

  return selectors;
})();

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
    // guarding. 171 .html templates at the time of writing, plus phase 40's five inline ones.
    expect(entries.length).toBeGreaterThan(140);
    expect(inlineEntries.length, 'the inline-template glob matched nothing').toBeGreaterThanOrEqual(5);

    // A truncated template literal would make every assertion over it vacuous the same way an empty
    // stylesheet did in phase 34a. Each of these components renders a real control or landmark.
    for (const [path, template] of inlineEntries) {
      expect(template.length, `inline template read back near-empty: ${path}`).toBeGreaterThan(40);
    }
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
        // Both spellings of a bound id. `LABEL_FOR_BOUND` already accepted `[for]` *and*
        // `[attr.for]`, and this side accepted only `[id]` — so a control naming itself with
        // `[attr.id]` read as unnamed. It went unnoticed because the one component that spells it
        // that way is allow-listed below; phase 40's own `lookup-filter` was the second, and the
        // guard reported it the moment the inline-template widening let the guard see it at all.
        const boundId = /\[(?:attr\.)?id\]="([^"]+)"/.exec(attrs)?.[1]?.trim();

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

  it('names every ARIA grouping it declares (WCAG 4.1.2)', () => {
    // Phase 40. 25 containers declared `role="group"` / `role="tablist"` and named none of them.
    // An unnamed group announces "group" and nothing else: it adds a boundary a screen-reader user
    // has to cross with no information about what is inside, which is worse than the plain <div> it
    // would otherwise have been. 34a asked whether every *control* was named; nobody had asked the
    // same question of a *grouping*.
    const found: string[] = [];
    const GROUPING = /<[a-z][a-z0-9-]*\b((?:[^>"]|"[^"]*")*?)role="(group|radiogroup|toolbar|region|tablist)"((?:[^>"]|"[^"]*")*?)\/?>/gs;

    for (const [path, source] of entries) {
      for (const m of matches(source, GROUPING)) {
        const attrs = m[1] + m[3];
        if (attrs.includes('aria-label') || attrs.includes('aria-labelledby')) {
          continue;
        }
        found.push(`${path}  role="${m[2]}"  ${m[0].replace(/\s+/g, ' ').slice(0, 70)}`);
      }
    }

    expect(
      found,
      report(
        found,
        'An ARIA grouping must say what it groups: add aria-label, or aria-labelledby pointing at ' +
          'the caption already above it. If there is nothing to say, drop the role instead.',
      ),
    ).toEqual([]);
  });

  it('routes every status message through the one live region (WCAG 4.1.3)', () => {
    // Phase 40, and the reason `app-status-banner` exists. A hand-written `role="alert"` inside an
    // `@if` is a live region that comes into existence already holding its text, which is one DOM
    // mutation and no change to any region a screen reader was watching — so nothing is announced.
    // 163 of them were spelled that way. The component keeps the region outside the `@if`; this
    // assertion is what stops the 164th being written by hand.
    const found: string[] = [];

    for (const [path, source] of entries) {
      if (path.endsWith('/shared/a11y/status-banner.ts')) {
        continue;
      }
      for (const m of matches(source, /role="(alert|status)"/g)) {
        // A Bootstrap spinner carries role="status" as its *own* name, with no live text.
        if (m[1] === 'status' && /spinner-border|visually-hidden/.test(source.slice(Math.max(0, m.index - 160), m.index + 160))) {
          continue;
        }
        found.push(`${path}  ${m[0]}`);
      }
    }

    expect(
      found,
      report(
        found,
        'Render <app-status-banner [message]="…" /> instead — unconditionally, not inside an @if. ' +
          'A live region only announces a change to contents it already had, so the region has to ' +
          'exist before the message does.',
      ),
    ).toEqual([]);
  });

  it('never nests an interactive control inside a link or a button (WCAG 4.1.2, 2.1.1)', () => {
    // Phase 47. Four document grids rendered a `<select>` inside the row's `<a [routerLink]>`.
    // Phase 45 found it as a *navigation* bug and fixed the navigation; the nesting itself is the
    // accessibility defect, and it is not one a browser recovers from. An interactive element
    // inside a link is folded into that link's accessible name, so the row announces as one
    // control with the options read out as its label; the keyboard user who tabs onto the select
    // is inside a link they never chose to enter; and HTML's parser is entitled to move the
    // element out of the anchor entirely, which is how the same markup behaves differently in two
    // browsers.
    //
    // The interesting half is CONTROL_COMPONENTS, derived rather than listed: a scan for
    // `<select>` inside `<a>` cannot see `<app-custom-status-picker>`, which is exactly the shape
    // that shipped. So the selectors of every component whose own template renders a control are
    // collected first, and a nested one of those counts the same as a nested `<select>`.
    const found: string[] = [];

    for (const [path, source] of entries) {
      for (const m of matches(source, CLICKABLE)) {
        const inner = m[3];
        const nested = [...matches(inner, /<(input|select|textarea|button|a)\b/g).map((c) => `<${c[1]}>`)];

        for (const selector of CONTROL_COMPONENTS) {
          if (new RegExp(`<${selector}\\b`).test(inner)) {
            nested.push(`<${selector}>`);
          }
        }

        if (nested.length > 0) {
          found.push(`${path}  <${m[1]}> contains ${[...new Set(nested)].join(', ')}`);
        }
      }
    }

    expect(
      found,
      report(
        found,
        'Take the control out of the link. The row pattern that works is a <div> carrying the ' +
          'row classes, an <a class="stretched-link"> on the row\'s title for the click target, ' +
          'and the controls as siblings with position-relative z-2 so they sit above the ' +
          'overlay — see any of the four document grids.',
      ),
    ).toEqual([]);
  });

  it('associates every field-level error with the control it is about (WCAG 3.3.1, 1.3.1)', () => {
    // Phase 47 (phase 40 carried item #2). This is 34a's mirror question — *which labels name no
    // control?* — asked of messages instead: which error messages name no control, and which
    // failing controls name no message. Both directions matter, and they fail differently. A
    // `fail()` with no `<app-field-error>` announces in the banner and marks a control the screen
    // reader then reads with a dangling `aria-describedby`; an `<app-field-error>` no `fail()` ever
    // names is an element that never renders, which looks like working markup forever.
    //
    // Keyed on the control's DOM id throughout, which is what makes both ends checkable from the
    // source at all: the message's id, the `aria-describedby` that points at it and the `fail()`
    // call are all derived from that one string.
    const found: string[] = [];
    let pairs = 0;

    for (const [path, raw] of Object.entries(templates)) {
      const template = raw.replace(HTML_COMMENT, '');
      const component = inlineTemplates[path.replace(/\.html$/, '.ts')] ?? '';

      const failed = new Set(matches(component, /fieldError\.fail\('([a-z0-9-]+)'/g).map((m) => m[1]));
      const described = new Set(
        matches(template, /<app-field-error\b(?:[^>"]|"[^"]*")*?\bcontrol="([a-z0-9-]+)"/gs).map((m) => m[1]),
      );

      for (const control of failed) {
        if (!described.has(control)) {
          found.push(`${path}  fails at '${control}' and renders no <app-field-error> for it`);
        }
      }

      for (const control of described) {
        if (!failed.has(control)) {
          found.push(`${path}  renders <app-field-error control="${control}"> that nothing ever sets`);
          continue;
        }

        if (!new RegExp(`id="${control}"`).test(template)) {
          found.push(`${path}  names control '${control}', which is not an id in this template`);
        }

        for (const binding of [
          `[class.is-invalid]="fieldError.is('${control}')"`,
          `[attr.aria-invalid]="fieldError.invalid('${control}')"`,
          `[attr.aria-describedby]="fieldError.describedBy('${control}')"`,
        ]) {
          if (!template.includes(binding)) {
            found.push(`${path}  '${control}' is missing ${binding}`);
          }
        }

        pairs += 1;
      }
    }

    expect(
      found,
      report(
        found,
        'A field-level error has three parts that have to agree: the fail() that sets it, the ' +
          'three bindings on the control, and the <app-field-error> the aria-describedby points ' +
          'at. See any document form for the shape.',
      ),
    ).toEqual([]);

    // Non-vacuity: this assertion would pass over an empty app. Sixteen pairs across thirteen
    // document forms at the time of writing.
    expect(pairs, 'the field-error scan found no associated fields at all').toBeGreaterThanOrEqual(16);
  });

  it('scans markup and not the prose in comments, without losing the markup around them', () => {
    // Phase 47. The stripping above is what stops a comment explaining a rule from failing that
    // rule, and both halves need saying: that comments are gone, and that nothing *else* is. The
    // second half is the one that would fail if the pattern were greedy — a single `[\s\S]*` would
    // swallow everything between the first `<!--` and the last `-->` in a template, which on these
    // pages is most of the file, and every other assertion here would quietly go vacuous.
    const quotation = entries.find(([path]) => path.endsWith('/quotation-list-page.html'))![1];

    expect(quotation).not.toContain('<!--');
    expect(quotation).not.toContain('invalid HTML that no event handler makes');
    expect(quotation).toContain('<app-custom-status-picker');
    expect(quotation).toContain('stretched-link');
  });

  it('finds the control-bearing components that the nesting check is derived from', () => {
    // Without this the derivation could quietly collapse to an empty set and the check above would
    // pass vacuously for every component-shaped control — which is the exact hole it exists to
    // close. `app-custom-status-picker` is named because it is the one that shipped nested.
    expect(CONTROL_COMPONENTS.size).toBeGreaterThan(10);
    expect(CONTROL_COMPONENTS.has('app-custom-status-picker')).toBe(true);
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

  it('measures every stock focus ring as failing 1.4.11, which is why the app paints its own', () => {
    // Phase 40, and the finding that only a keyboard produces: every control in the content area
    // was focused, visibly, and the ring was almost not there. Bootstrap paints `:focus-visible` as
    // the control's own tone at 50% alpha; 34a already knew those tones clear 4.5:1 against pure
    // white "and only just", and half of almost-enough is nothing. The numbers below are the whole
    // argument for replacing the ring rather than tuning it — no variant is close to 3:1, so there
    // was nothing to tune.
    const stock = FOCUS_RING_RULES.filter((r) => r.variant !== '');
    expect(stock.length).toBe(12);
    expect(stock.every((r) => !r.passesAA), stock.map((r) => `${r.variant} ${r.ratio.toFixed(2)}:1`).join(', ')).toBe(true);
    expect(Math.max(...stock.map((r) => r.ratio))).toBeLessThan(AA_NON_TEXT);

    const own = FOCUS_RING_RULES.find((r) => r.variant === '')!;
    expect(own.ratio, "the app's own ring must clear 3:1 on both the card and the page body").toBeGreaterThanOrEqual(
      AA_NON_TEXT,
    );
  });

  it('paints that ring in the shipped stylesheet, on :focus-visible, with the box-shadow cleared', () => {
    // The same trap as the palette test below: `contrast-rules.ts` would go on reporting 6.11:1 for
    // a ring `styles.scss` had stopped painting, and every assertion above would stay green.
    const stylesheet = readStylesheet();
    expect(stylesheet.length, 'src/styles.scss read back empty').toBeGreaterThan(500);

    const rule = /:focus-visible\s*\{[\s\S]*?\}/.exec(stylesheet)?.[0] ?? '';
    expect(rule, 'styles.scss no longer has a :focus-visible rule at all').not.toBe('');
    expect(rule).toContain(`${FOCUS_RING.widthPx}px solid ${FOCUS_RING.color}`);
    expect(rule, "Bootstrap's own ring must be cleared, or two indicators stack").toContain('box-shadow: none');
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
