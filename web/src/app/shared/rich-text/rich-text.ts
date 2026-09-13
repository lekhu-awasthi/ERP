/**
 * Phase 39 — the client half of `Domain/Common/RichText.cs`.
 *
 * <b>Why there are two.</b> The server's copy is the one that decides what gets stored, and it would
 * be sufficient on its own for safety. This one exists so that what the user sees while typing is
 * what will be stored: without it, pasting a coloured table from Word shows a coloured table, the
 * save silently returns a plain paragraph, and the field appears to have eaten the content. The
 * server remains authoritative — nothing here is trusted by anything.
 *
 * <b>How the two are kept in step.</b> `rich-text-cases.json` sits beside this file and is read by
 * both suites: by `rich-text.spec.ts` here, and by `RichTextSharedCasesTests` in Domain.UnitTests,
 * which links the same file in as an embedded resource. That is phase-26b's rule for `BsCalendar`
 * and its `bs-date.ts` twin — port it, never retype it, and pin both boundaries — applied to the
 * second pair of twins in this codebase.
 *
 * <b>The same security property as the server's.</b> This is re-emission, not filtering: the input
 * is parsed with `DOMParser` (which builds a detached document — it runs no script and fetches no
 * resource), walked, and written back out of string constants this file owns, with text escaped.
 * No attribute the user supplied is ever copied; the one attribute emitted is a `text-align` chosen
 * from four constants.
 */

/** The four character styles, matching `RichTextStyle`. */
const BOLD = 1;
const ITALIC = 2;
const UNDERLINE = 4;
const STRIKE = 8;

type Alignment = 'left' | 'center' | 'right' | 'justify';

/** Elements whose content is discarded with them, matching `RawParser.Opaque`. */
const OPAQUE = new Set([
  'SCRIPT', 'STYLE', 'IFRAME', 'OBJECT', 'EMBED', 'SVG', 'MATH', 'NOSCRIPT', 'TEMPLATE',
  'TEXTAREA', 'TITLE', 'HEAD', 'FRAME', 'FRAMESET', 'APPLET',
]);

/** Elements that end the paragraph in progress, matching `Normalizer.Blocks`. */
const BLOCKS = new Set([
  'P', 'DIV', 'BLOCKQUOTE', 'PRE', 'SECTION', 'ARTICLE', 'HEADER', 'FOOTER', 'MAIN', 'ASIDE',
  'FIGURE', 'FIGCAPTION', 'ADDRESS', 'DL', 'DD', 'DT', 'TR', 'CAPTION', 'FORM', 'FIELDSET',
  'H1', 'H2', 'H3', 'H4', 'H5', 'H6',
]);

const HEADINGS = new Set(['H1', 'H2', 'H3', 'H4', 'H5', 'H6']);

const EMPHASES: Readonly<Record<string, number>> = {
  B: BOLD, STRONG: BOLD,
  I: ITALIC, EM: ITALIC,
  U: UNDERLINE, INS: UNDERLINE,
  S: STRIKE, STRIKE: STRIKE, DEL: STRIKE,
};

/** One paragraph or list item under construction. */
interface Flow {
  readonly parts: string[];
  visible: boolean;
  breaks: boolean;
}

/** Canonical, safe HTML, or '' when the input carries no content. Mirrors `RichText.Sanitize`. */
export function sanitizeRichText(html: string | null | undefined): string {
  if (!html || html.trim().length === 0) {
    return '';
  }

  const parsed = new DOMParser().parseFromString(html, 'text/html');
  const writer = new BlockWriter();
  walk(parsed.body, writer, 'left', 0);
  writer.flush();

  return writer.finish();
}

/** True when the field would store nothing. Mirrors `RichText.IsEmpty`. */
export function isRichTextEmpty(html: string | null | undefined): boolean {
  return sanitizeRichText(html).length === 0;
}

/** The field's text with markup removed, for a preview line or a title attribute. */
export function richTextToPlainText(html: string | null | undefined): string {
  const parsed = new DOMParser().parseFromString(sanitizeRichText(html), 'text/html');

  return (parsed.body.textContent ?? '').trim();
}

/**
 * Wraps plain text as rich text, escaping it. Mirrors `RichText.FromPlainText` — used when a caller
 * has plain text and needs to hand it to the editor.
 */
export function richTextFromPlainText(text: string | null | undefined): string {
  if (!text || text.trim().length === 0) {
    return '';
  }

  return text
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .split('\n\n')
    .filter((paragraph) => paragraph.trim().length > 0)
    .map((paragraph) => `<p>${paragraph.split('\n').map(escape).join('<br />')}</p>`)
    .join('');
}

// --- the walk -------------------------------------------------------------------------------

class BlockWriter {
  private readonly blocks: string[] = [];

  private flow: Flow = { parts: [], visible: false, breaks: false };

  alignment: Alignment = 'left';

  write(markup: string, visible: boolean): void {
    this.flow.parts.push(markup);
    this.flow.visible ||= visible;
  }

  writeBreak(): void {
    this.flow.parts.push('<br />');
    this.flow.breaks = true;
  }

  addBlock(markup: string): void {
    this.blocks.push(markup);
  }

  /** Ends the paragraph in progress. A paragraph of only whitespace is dropped; one holding only
   * breaks becomes the empty paragraph that `finish` may then trim. */
  flush(): void {
    const { parts, visible, breaks } = this.flow;
    this.flow = { parts: [], visible: false, breaks: false };

    if (parts.length === 0) {
      return;
    }

    if (!visible) {
      if (breaks) {
        this.blocks.push('<p></p>');
      }

      return;
    }

    const body = parts.join('').replace(/^(\s|&nbsp;)+/, '').replace(/(\s|&nbsp;)+$/, '');

    if (body.length === 0) {
      return;
    }

    this.blocks.push(this.alignment === 'left' ? `<p>${body}</p>` : `<p style="text-align:${this.alignment}">${body}</p>`);
  }

  /** Trims empty paragraphs at the two ends only — the editor emits a trailing one almost every
   * time, while one between two filled paragraphs is a blank line somebody typed. */
  finish(): string {
    let start = 0;
    let end = this.blocks.length;

    while (start < end && this.blocks[start] === '<p></p>') {
      start++;
    }

    while (end > start && this.blocks[end - 1] === '<p></p>') {
      end--;
    }

    return this.blocks.slice(start, end).join('');
  }

  /** Renders one list item's inline content, reusing the same flow machinery. */
  static inline(node: Node, alignment: Alignment, style: number): string {
    const inner = new BlockWriter();
    inner.alignment = alignment;
    walkChildren(node, inner, alignment, style);
    inner.flush();

    return inner.blocks
      .map((block) => block.replace(/^<p[^>]*>/, '').replace(/<\/p>$/, ''))
      .filter((x) => x.length > 0)
      .join('<br />');
  }
}

function walk(node: Node, writer: BlockWriter, alignment: Alignment, style: number): void {
  if (node.nodeType === Node.TEXT_NODE) {
    const value = (node.nodeValue ?? '').replace(/\s+/g, ' ');

    if (value.length > 0) {
      writer.write(styled(escape(value), style), value.trim().length > 0);
    }

    return;
  }

  if (node.nodeType !== Node.ELEMENT_NODE) {
    return;
  }

  const element = node as Element;
  const name = element.tagName.toUpperCase();

  if (OPAQUE.has(name)) {
    return;
  }

  const inherited = alignmentOf(element) ?? alignment;

  if (name === 'BR') {
    writer.writeBreak();

    return;
  }

  if (name === 'HR') {
    writer.flush();
    writer.addBlock('<hr />');

    return;
  }

  if (name === 'UL' || name === 'OL') {
    writer.flush();
    const tag = name === 'OL' ? 'ol' : 'ul';
    const items = Array.from(element.children)
      .filter((child) => child.tagName.toUpperCase() === 'LI')
      .map((item) => `<li>${BlockWriter.inline(item, alignmentOf(item) ?? inherited, style)}</li>`);

    if (items.length > 0) {
      writer.addBlock(`<${tag}>${items.join('')}</${tag}>`);
    }

    return;
  }

  const emphasis = EMPHASES[name];

  if (emphasis !== undefined) {
    walkChildren(element, writer, inherited, style | emphasis);

    return;
  }

  if (BLOCKS.has(name)) {
    writer.flush();
    const previous = writer.alignment;
    writer.alignment = inherited;
    walkChildren(element, writer, inherited, HEADINGS.has(name) ? style | BOLD : style);
    writer.flush();
    writer.alignment = previous;

    return;
  }

  if (name === 'TD' || name === 'TH') {
    walkChildren(element, writer, inherited, style);
    writer.write(' ', false);

    return;
  }

  if (name === 'LI') {
    // A list item outside any list: a paragraph, rather than text thrown away.
    writer.flush();
    walkChildren(element, writer, inherited, style);
    writer.flush();

    return;
  }

  // Everything else unwraps — span, font, a, img, table, tbody, unknown tags. See the file comment.
  walkChildren(element, writer, inherited, style);
}

function walkChildren(node: Node, writer: BlockWriter, alignment: Alignment, style: number): void {
  node.childNodes.forEach((child) => walk(child, writer, alignment, style));
}

/** Wraps one escaped run in a tag per set flag, outermost first — the order the server emits. */
function styled(markup: string, style: number): string {
  let result = markup;

  if (style & STRIKE) {
    result = `<s>${result}</s>`;
  }

  if (style & UNDERLINE) {
    result = `<u>${result}</u>`;
  }

  if (style & ITALIC) {
    result = `<em>${result}</em>`;
  }

  if (style & BOLD) {
    result = `<strong>${result}</strong>`;
  }

  return result;
}

/**
 * Reduces a declared alignment to one of four constants, or null. The value is matched and then
 * discarded: what reaches the output is the constant that matched, never the string.
 */
function alignmentOf(element: Element): Alignment | null {
  const declared =
    (element as HTMLElement).style?.textAlign ||
    element.getAttribute('align') ||
    '';

  switch (declared.trim().toLowerCase()) {
    case 'center':
      return 'center';
    case 'right':
    case 'end':
      return 'right';
    case 'justify':
      return 'justify';
    case 'left':
    case 'start':
      return 'left';
    default:
      return null;
  }
}

function escape(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
