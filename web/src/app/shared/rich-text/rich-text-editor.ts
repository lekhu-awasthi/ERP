import {
  Component,
  ElementRef,
  computed,
  effect,
  input,
  model,
  signal,
  untracked,
  viewChild,
} from '@angular/core';

import { sanitizeRichText } from './rich-text';

/** One toolbar button: the `execCommand` it runs, its label, and its Bootstrap icon. */
interface Tool {
  readonly command: string;
  readonly value?: string;
  readonly label: string;
  readonly icon: string;
  /** Whether the button reports pressed/unpressed state, for `aria-pressed`. */
  readonly toggle: boolean;
}

/**
 * Phase 39 — the rich-text control, and `app-terms-editor`'s and the Send Email body's one seam.
 *
 * <b>One control in two hosts is an observation, not a preference.</b> The reference product mounts
 * TinyMCE 7.1.1 in both places with a byte-identical `toolbar` option string — read off the live
 * tenant on 2026-09-13 from `tinymce.activeEditor.options.get('toolbar')` in each. Phases 27b and 30
 * each recorded "a textarea standing in for the rich editor" separately and each named this seam;
 * they turn out to have been describing one component.
 *
 * <b>Why this is hand-built rather than TinyMCE.</b> `package.json` carries Bootstrap and RxJS and
 * nothing else, the initial bundle is already over its budget (phase 34b), and — the deciding
 * reason — every button here has to correspond to something `RichTextPdfRenderer` can draw. An
 * editor offering colour, tables and images would be offering formatting the printed copy silently
 * drops, and the printed copy is the one a customer receives. The toolbar is therefore the reference
 * product's minus what QuestPDF cannot honour; Decision B in docs/phase-39-status.md states the
 * divergence and the four buttons it costs.
 *
 * <b>`execCommand` is deprecated and is still the right call.</b> It is the only way to edit a
 * `contenteditable` selection without shipping a selection/range engine, every current browser
 * implements it, and — crucially — nothing depends on what it produces: whatever markup it leaves
 * behind goes through {@link sanitizeRichText} before it becomes the model's value, and through the
 * server's own sanitiser before it is stored. If a browser drops it, this file changes and nothing
 * else does.
 *
 * <b>Zoneless notes.</b> The editable region is never re-rendered from the model while it has focus
 * — writing `innerHTML` under a caret moves the caret to the start, which reads as the field
 * scrambling itself as you type. The model is written on input; the DOM is written only when the
 * value arrives from somewhere else. The `effect` that does it is `untracked` on its write half so
 * it cannot re-enter (phase-35a's NG0600 lesson).
 */
@Component({
  selector: 'app-rich-text-editor',
  imports: [],
  templateUrl: './rich-text-editor.html',
  styleUrl: './rich-text-editor.scss',
})
export class RichTextEditor {
  readonly disabled = input(false);

  /** Names the editable region for a screen reader. There is no `<label for>` that can reach a
   * `contenteditable` div, which is phase-34a's "a sweep over inputs cannot see a component" in its
   * most literal form — so the control labels itself. */
  readonly ariaLabel = input('Rich text');

  readonly rows = input(5);

  /** Two-way: the host owns the value and sends it with its own save. Always sanitised markup. */
  readonly value = model<string>('');

  private readonly surface = viewChild<ElementRef<HTMLDivElement>>('surface');

  /** What this component last wrote to the model, so an echo of its own value is not treated as an
   * external change and does not reset the caret. */
  private lastEmitted = '';

  protected readonly focused = signal(false);

  protected readonly minHeight = computed(() => `${Math.max(2, this.rows()) * 1.5}rem`);

  /**
   * The toolbar, in the reference product's own order: character styles, alignment, lists, rule.
   * `justifyFull` is present because `RichTextPdfRenderer` can honour it; colour, font, size, table
   * and image are absent because it cannot.
   */
  protected readonly styleTools: readonly Tool[] = [
    { command: 'bold', label: 'Bold', icon: 'bi-type-bold', toggle: true },
    { command: 'italic', label: 'Italic', icon: 'bi-type-italic', toggle: true },
    { command: 'underline', label: 'Underline', icon: 'bi-type-underline', toggle: true },
    { command: 'strikeThrough', label: 'Strikethrough', icon: 'bi-type-strikethrough', toggle: true },
  ];

  protected readonly alignTools: readonly Tool[] = [
    { command: 'justifyLeft', label: 'Align left', icon: 'bi-text-left', toggle: true },
    { command: 'justifyCenter', label: 'Align centre', icon: 'bi-text-center', toggle: true },
    { command: 'justifyRight', label: 'Align right', icon: 'bi-text-right', toggle: true },
    { command: 'justifyFull', label: 'Justify', icon: 'bi-justify', toggle: true },
  ];

  protected readonly listTools: readonly Tool[] = [
    { command: 'insertUnorderedList', label: 'Bullet list', icon: 'bi-list-ul', toggle: true },
    { command: 'insertOrderedList', label: 'Numbered list', icon: 'bi-list-ol', toggle: true },
    { command: 'insertHorizontalRule', label: 'Horizontal line', icon: 'bi-dash-lg', toggle: false },
  ];

  /** Bumped after every command and selection change so the `aria-pressed` bindings re-read
   * `queryCommandState` — which is DOM state the signal graph cannot observe on its own. */
  protected readonly selectionTick = signal(0);

  constructor() {
    effect(() => {
      const incoming = this.value();
      const element = untracked(() => this.surface()?.nativeElement);

      if (!element || incoming === this.lastEmitted) {
        return;
      }

      // Only when the value came from outside: a re-render mid-edit would move the caret.
      if (!untracked(() => this.focused())) {
        element.innerHTML = sanitizeRichText(incoming);
        this.lastEmitted = incoming;
      }
    });
  }

  protected isActive(tool: Tool): boolean {
    this.selectionTick();

    if (!tool.toggle || typeof document.queryCommandState !== 'function') {
      return false;
    }

    try {
      return document.queryCommandState(tool.command);
    } catch {
      // Firefox throws for an unsupported command rather than returning false.
      return false;
    }
  }

  protected run(tool: Tool): void {
    if (this.disabled()) {
      return;
    }

    this.surface()?.nativeElement.focus();
    document.execCommand(tool.command, false, tool.value);
    this.selectionTick.update((x) => x + 1);
    this.emit();
  }

  protected onInput(): void {
    this.emit();
  }

  protected onSelectionChange(): void {
    this.selectionTick.update((x) => x + 1);
  }

  protected onFocus(): void {
    this.focused.set(true);
  }

  /**
   * On blur the surface is re-rendered from the sanitised value. That is the moment the user sees
   * what will actually be stored — a pasted table collapsing to paragraphs here, while they are
   * still looking at the field, rather than after a save and a reload.
   */
  protected onBlur(): void {
    this.focused.set(false);
    const element = this.surface()?.nativeElement;

    if (element) {
      const clean = sanitizeRichText(element.innerHTML);
      element.innerHTML = clean;
      this.lastEmitted = clean;
      this.value.set(clean);
    }
  }

  /**
   * Paste is the one path where unsanitised markup would otherwise reach the document at all — a
   * copy from a web page brings scripts, styles and event handlers with it. The clipboard's HTML is
   * sanitised and inserted; falling back to its plain text when there is no HTML flavour.
   */
  protected onPaste(event: ClipboardEvent): void {
    if (this.disabled()) {
      return;
    }

    event.preventDefault();

    const html = event.clipboardData?.getData('text/html') ?? '';
    const text = event.clipboardData?.getData('text/plain') ?? '';
    const clean = html.length > 0 ? sanitizeRichText(html) : escapeToParagraphs(text);

    document.execCommand('insertHTML', false, clean);
    this.emit();
  }

  private emit(): void {
    const element = this.surface()?.nativeElement;

    if (!element) {
      return;
    }

    const clean = sanitizeRichText(element.innerHTML);
    this.lastEmitted = clean;
    this.value.set(clean);
  }
}

/** Plain clipboard text, escaped and split into paragraphs — the no-HTML-flavour paste path. */
function escapeToParagraphs(text: string): string {
  return text
    .replace(/\r\n/g, '\n')
    .split('\n\n')
    .filter((paragraph) => paragraph.trim().length > 0)
    .map(
      (paragraph) =>
        `<p>${paragraph
          .split('\n')
          .map((line) =>
            line
              .replace(/&/g, '&amp;')
              .replace(/</g, '&lt;')
              .replace(/>/g, '&gt;'),
          )
          .join('<br />')}</p>`,
    )
    .join('');
}
