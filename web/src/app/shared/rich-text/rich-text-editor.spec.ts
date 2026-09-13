import { Component, signal } from '@angular/core';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RichTextEditor } from './rich-text-editor';

@Component({
  imports: [RichTextEditor],
  template: `<app-rich-text-editor
    ariaLabel="Terms and conditions"
    [disabled]="disabled()"
    [(value)]="value"
  />`,
})
class Host {
  readonly value = signal('');
  readonly disabled = signal(false);
}

/**
 * Phase 39. The editor's own tests, on top of `rich-text.spec.ts`'s tests of the grammar.
 *
 * The one the phase owes by name is
 * {@link stored_markup_is_rendered_inert}: a script tag in stored content must not execute. It is
 * asserted structurally rather than by watching for a side effect, because jsdom does not load
 * images or run inline scripts from `innerHTML` anyway — a side-effect test would pass in this
 * environment no matter how broken the sanitiser was, which is the vacuous-guard failure phase 34a
 * warns about. What is asserted instead is that the rendered subtree contains no element and no
 * attribute through which a browser *could* execute anything.
 */
describe('RichTextEditor', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;

  /** jsdom implements neither, and the component is written so that neither is load-bearing. */
  beforeEach(async () => {
    document.execCommand = (() => true) as typeof document.execCommand;
    document.queryCommandState = (() => false) as typeof document.queryCommandState;

    await TestBed.configureTestingModule({
      imports: [Host],
      providers: [provideZonelessChangeDetection()],
    }).compileComponents();

    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
  });

  function surface(): HTMLDivElement {
    return fixture.nativeElement.querySelector('.rich-text-surface') as HTMLDivElement;
  }

  it('renders the stored value into the editable surface', () => {
    host.value.set('<p>Net <strong>30</strong> days.</p>');
    fixture.detectChanges();

    expect(surface().innerHTML).toBe('<p>Net <strong>30</strong> days.</p>');
  });

  it('stored markup is rendered inert', () => {
    // What a Source-code box in the reference product's editor can produce, arriving from the
    // server as though it had been stored before phase 39 existed.
    host.value.set(
      '<p>Terms</p><script>window.alert(1)</script><img src="x" onerror="window.alert(1)">' +
        '<a href="javascript:window.alert(1)">click</a><iframe src="javascript:1"></iframe>',
    );
    fixture.detectChanges();

    const region = surface();

    expect(region.querySelectorAll('script, img, iframe, object, embed, svg, style')).toHaveLength(0);
    expect(region.querySelectorAll('a[href]')).toHaveLength(0);

    for (const element of Array.from(region.querySelectorAll('*'))) {
      for (const attribute of Array.from(element.attributes)) {
        expect(attribute.name.toLowerCase().startsWith('on')).toBe(false);
        expect(attribute.value.toLowerCase()).not.toContain('javascript:');
      }
    }

    // The prose survives, which is the other half: an inert field that also lost its content would
    // be a different bug wearing the same green test.
    expect(region.textContent).toContain('Terms');
    expect(region.textContent).toContain('click');
  });

  it('writes back the sanitized markup, not what was typed', () => {
    fixture.detectChanges();

    const region = surface();
    region.innerHTML = '<div><font color="red">typed</font></div><script>window.alert(1)</script>';
    region.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(host.value()).toBe('<p>typed</p>');
  });

  it('sanitizes pasted HTML before it reaches the document', () => {
    fixture.detectChanges();

    let inserted: string | undefined;
    document.execCommand = ((command: string, _ui: boolean, value?: string) => {
      if (command === 'insertHTML') {
        inserted = value;
      }

      return true;
    }) as typeof document.execCommand;

    const paste = new Event('paste', { cancelable: true }) as ClipboardEvent;
    Object.defineProperty(paste, 'clipboardData', {
      value: {
        getData: (type: string) =>
          type === 'text/html' ? '<p onclick="alert(1)">pasted<script>alert(1)</script></p>' : '',
      },
    });

    surface().dispatchEvent(paste);

    expect(inserted).toBe('<p>pasted</p>');
    expect(paste.defaultPrevented).toBe(true);
  });

  it('escapes a plain-text paste rather than letting it become markup', () => {
    fixture.detectChanges();

    let inserted: string | undefined;
    document.execCommand = ((command: string, _ui: boolean, value?: string) => {
      if (command === 'insertHTML') {
        inserted = value;
      }

      return true;
    }) as typeof document.execCommand;

    const paste = new Event('paste', { cancelable: true }) as ClipboardEvent;
    Object.defineProperty(paste, 'clipboardData', {
      value: { getData: (type: string) => (type === 'text/plain' ? '<b>not bold</b>' : '') },
    });

    surface().dispatchEvent(paste);

    expect(inserted).toBe('<p>&lt;b&gt;not bold&lt;/b&gt;</p>');
  });

  it('re-renders the surface from the sanitized value on blur, so the user sees what will be stored', () => {
    fixture.detectChanges();

    const region = surface();
    region.innerHTML = '<table><tbody><tr><td>Item</td><td>Rate</td></tr></tbody></table>';
    region.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(region.innerHTML).toBe('<p>Item Rate</p>');
    expect(host.value()).toBe('<p>Item Rate</p>');
  });

  // --- accessibility (phase 34a) ------------------------------------------------------------

  it('names itself, since no label element can point at a contenteditable div', () => {
    fixture.detectChanges();

    const region = surface();

    expect(region.getAttribute('role')).toBe('textbox');
    expect(region.getAttribute('aria-multiline')).toBe('true');
    expect(region.getAttribute('aria-label')).toBe('Terms and conditions');
  });

  it('sets aria-pressed on every toggle button itself, because Bootstrap JS is not loaded', () => {
    fixture.detectChanges();

    const toggles = Array.from(
      fixture.nativeElement.querySelectorAll('.rich-text-toolbar button'),
    ) as HTMLButtonElement[];

    expect(toggles.length).toBeGreaterThan(0);

    for (const button of toggles) {
      expect(button.getAttribute('aria-label')).toBeTruthy();
    }

    // The horizontal rule is an action, not a state, and correctly reports no pressed state.
    const rule = toggles.find((x) => x.getAttribute('aria-label') === 'Horizontal line')!;
    expect(rule.getAttribute('aria-pressed')).toBeNull();

    const bold = toggles.find((x) => x.getAttribute('aria-label') === 'Bold')!;
    expect(bold.getAttribute('aria-pressed')).toBe('false');
  });

  it('offers only the formatting the print pipeline can draw', () => {
    fixture.detectChanges();

    const labels = Array.from(
      fixture.nativeElement.querySelectorAll('.rich-text-toolbar button'),
    ).map((x) => (x as HTMLElement).getAttribute('aria-label'));

    expect(labels).toEqual([
      'Bold',
      'Italic',
      'Underline',
      'Strikethrough',
      'Align left',
      'Align centre',
      'Align right',
      'Justify',
      'Bullet list',
      'Numbered list',
      'Horizontal line',
    ]);

    // Decision B, asserted in the negative: the four the reference product offers and QuestPDF
    // cannot honour must stay absent, or somebody will add one and the PDF will quietly drop it.
    for (const absent of ['Text color', 'Font', 'Font size', 'Table', 'Insert/edit image']) {
      expect(labels).not.toContain(absent);
    }
  });

  // --- read-only ----------------------------------------------------------------------------

  it('shows the terms of an approved document with no toolbar and no editing', () => {
    host.value.set('<p>Net 30 days.</p>');
    host.disabled.set(true);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.rich-text-toolbar')).toBeNull();

    const region = surface();
    expect(region.getAttribute('contenteditable')).toBeNull();
    expect(region.getAttribute('aria-readonly')).toBe('true');
    expect(region.textContent).toContain('Net 30 days.');
  });
});
