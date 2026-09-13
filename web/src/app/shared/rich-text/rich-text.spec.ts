/// <reference types="vite/client" />

import { isRichTextEmpty, richTextFromPlainText, richTextToPlainText, sanitizeRichText } from './rich-text';

/**
 * Phase 39 — the client half of the rich-text contract.
 *
 * The first block reads `rich-text-cases.json`, the same file `RichTextSharedCasesTests` links into
 * the Domain suite as an embedded resource. Neither sanitiser is the source of truth for the other;
 * that file is. It has already earned its keep: it caught the two halves disagreeing about
 * collapsing runs of whitespace, which nothing else would have noticed until somebody pasted
 * indented markup and got a paragraph pushed across the page.
 *
 * The rest is the client's own security bar. It is not a lighter version of the server's — this
 * sanitiser decides what the user sees while typing, and a payload that survives here is one that
 * executes in the author's own browser before any save happens.
 */

const fixture = import.meta.glob('./rich-text-cases.json', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

interface SharedCase {
  readonly why: string;
  readonly input: string;
  readonly expected: string;
}

function sharedCases(): readonly SharedCase[] {
  const raw = fixture['./rich-text-cases.json'];

  // phase-34a's rule: a guard must assert its input is non-empty, not merely defined. Vite hands
  // back an empty string for a `?raw` import it could not resolve, and every assertion over it
  // would then pass vacuously.
  expect(typeof raw).toBe('string');
  expect(raw.length).toBeGreaterThan(500);

  return (JSON.parse(raw) as { cases: SharedCase[] }).cases;
}

describe('the shared rich-text contract', () => {
  it('has not lost most of its cases', () => {
    expect(sharedCases().length).toBeGreaterThanOrEqual(25);
  });

  for (const { why, input, expected } of sharedCases()) {
    it(`agrees with the server: ${why}`, () => {
      expect(sanitizeRichText(input)).toBe(expected);
    });
  }
});

describe('sanitizeRichText', () => {
  /** The same adversarial corpus the server's tests use, by the same reasoning. */
  const payloads = [
    '<script>alert(1)</script>',
    '<p onclick="alert(1)">hi</p>',
    '<p onmouseover=alert(1)>hi</p>',
    '<a href="javascript:alert(1)">click</a>',
    '<img src=x onerror=alert(1)>',
    '<svg><script>alert(1)</script></svg>',
    '<svg/onload=alert(1)>',
    '<iframe src="javascript:alert(1)"></iframe>',
    '<style>body{background:url(\'javascript:alert(1)\')}</style>',
    '<p style="width:expression(alert(1))">hi</p>',
    '<scr<script>ipt>alert(1)</script>',
    '<noscript><p title="</noscript><img src=x onerror=alert(1)>"></noscript>',
    '<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>',
    '<template><script>alert(1)</script></template>',
    '<base href="http://evil.test/">',
    '<meta http-equiv="refresh" content="0;url=javascript:alert(1)">',
    '<form><button formaction="javascript:alert(1)">go</button></form>',
  ];

  /**
   * The blanket property, matching the server's: the output's only attribute is the `text-align`
   * the emitter owns. A new evasion technique fails this without anyone having thought of it.
   */
  it.each(payloads)('emits no attribute it did not write, for %s', (payload) => {
    const output = sanitizeRichText(payload);

    for (const tag of output.match(/<[^>]*>/g) ?? []) {
      const legal =
        !tag.includes(' ') ||
        tag === '<br />' ||
        tag === '<hr />' ||
        /^<p style="text-align:(left|center|right|justify)">$/.test(tag);

      expect(legal, `unexpected tag ${tag} in ${output}`).toBe(true);
    }
  });

  it.each(payloads)('never yields markup a browser would execute, for %s', (payload) => {
    const output = sanitizeRichText(payload).toLowerCase();

    expect(output).not.toContain('<script');
    expect(output).not.toContain('javascript:');
    expect(output).not.toContain('onerror');
    expect(output).not.toContain('onclick');
    expect(output).not.toContain('onload');
  });

  it('drops script content rather than leaving it as visible prose', () => {
    expect(sanitizeRichText('<p>a</p><script>alert(1)</script>')).toBe('<p>a</p>');
  });

  it.each(payloads)('is idempotent for %s', (payload) => {
    const once = sanitizeRichText(payload);

    expect(sanitizeRichText(once)).toBe(once);
  });

  it('treats content that renders as nothing as empty', () => {
    for (const empty of [null, undefined, '', '   ', '<p></p>', '<p><br></p>', '<img src="x">']) {
      expect(sanitizeRichText(empty)).toBe('');
      expect(isRichTextEmpty(empty)).toBe(true);
    }
  });
});

describe('richTextToPlainText', () => {
  it('keeps the words and drops the markup', () => {
    expect(richTextToPlainText('<p>Net <strong>30</strong> days.</p>')).toBe('Net 30 days.');
  });

  it('decodes entities rather than printing them', () => {
    expect(richTextToPlainText('<p>Terms &amp; conditions</p>')).toBe('Terms & conditions');
  });
});

describe('richTextFromPlainText', () => {
  it('escapes markup characters instead of letting them become markup', () => {
    expect(richTextFromPlainText('a & b\nsecond\n\nthird')).toBe(
      '<p>a &amp; b<br />second</p><p>third</p>',
    );
  });

  it('produces something already sanitized', () => {
    const converted = richTextFromPlainText('a & b\nsecond');

    expect(sanitizeRichText(converted)).toBe(converted);
  });

  it('yields nothing for nothing', () => {
    expect(richTextFromPlainText('  \n ')).toBe('');
  });
});
