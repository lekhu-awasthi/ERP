import { Injectable, OnDestroy } from '@angular/core';

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), '
  + 'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Phase 95 -- keeps a keyboard user's place when the control they are on goes away, app-wide, in one place.
 *
 * <p><b>The two ways focus fell to `<body>`</b> (phase 93 § 5).</p>
 * <ul>
 *   <li><b>A Save that fails.</b> 56 screens disable their button while saving (`[disabled]="saving()"`). A focused
 *   control that becomes disabled loses focus to `<body>`, and when the save fails and the button comes back, nothing
 *   put focus back: a keyboard user was left at the top of the page.</li>
 *   <li><b>Remove.</b> A line's *Remove* button leaves with its line (about 15 line forms, and the smaller lists:
 *   attachments, contact people, e-mail recipients, quick links), and focus went with it.</li>
 * </ul>
 *
 * <p><b>The rule.</b> Act only when focus is on `<body>` (or nowhere). A screen that moves focus itself (to a field's
 * error, a dialog, the till's own rules since phase 85) does so inside Angular's render, before this observer runs, so
 * this sees focus somewhere and leaves it there. A disabled control gets focus back the moment it is enabled again. A
 * removed control hands focus to its twin (the same label: the next line's Remove) on the next row, else on the
 * previous row, else to the first control after where it was (a line table's Add Line), else the last one before.</p>
 *
 * <p>One observer rather than a fix on each of 70-odd screens, so a screen added tomorrow is covered too.</p>
 */
@Injectable({ providedIn: 'root' })
export class FocusKeeper implements OnDestroy {
  private root: Document | null = null;
  private lastFocused: HTMLElement | null = null;
  private parked: HTMLElement | null = null;
  private observer: MutationObserver | null = null;

  private readonly onFocusIn = (event: FocusEvent) => {
    if (event.target instanceof HTMLElement) {
      this.lastFocused = event.target;
      this.parked = null;
    }
  };

  /** Starts watching the document. Safe to call twice. */
  start(root: Document = document): void {
    if (this.observer || typeof MutationObserver === 'undefined') return;
    this.root = root;
    root.addEventListener('focusin', this.onFocusIn, true);
    this.observer = new MutationObserver((records) => records.forEach((x) => this.onMutation(x)));
    this.observer.observe(root.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['disabled'] });
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
    this.observer = null;
    this.root?.removeEventListener('focusin', this.onFocusIn, true);
  }

  private get focusIsLost(): boolean {
    const root = this.root!;
    return root.activeElement === null || root.activeElement === root.body;
  }

  private onMutation(record: MutationRecord): void {
    if (record.type === 'attributes') {
      this.onDisabledChanged(record.target as HTMLElement);
    } else if (record.removedNodes.length > 0) {
      this.onRemoved(record);
    }
  }

  private onDisabledChanged(element: HTMLElement): void {
    if (element.hasAttribute('disabled')) {
      if (element === this.lastFocused) this.parked = element;
      return;
    }

    if (element === this.parked) {
      this.parked = null;
      if (element.isConnected && this.focusIsLost) element.focus();
    }
  }

  private onRemoved(record: MutationRecord): void {
    const lost = this.lastFocused;
    if (!lost || lost.isConnected) return;
    if (![...record.removedNodes].some((node) => node === lost || node.contains(lost))) return;

    this.lastFocused = null;
    this.parked = null;
    if (!this.focusIsLost) return;

    const target = this.twin(nextElement(record.nextSibling), lost)
      ?? this.twin(previousElement(record.previousSibling), lost)
      ?? this.addButtonNear(record.target)
      ?? this.after(record)
      ?? this.before(record);
    target?.focus();
  }

  /** The list's own "Add ..." button (Add Line, Add Allocation), the nearest one around the emptied list: what a user
   * reaches for next, and where a line table's error already points (phase 48). It may sit above the table. */
  private addButtonNear(container: Node): HTMLElement | null {
    let scope: Element | null = container instanceof Element ? container : container.parentElement;
    for (let depth = 0; scope && depth < 6; depth++, scope = scope.parentElement) {
      const add = [...scope.querySelectorAll<HTMLButtonElement>('button:not([disabled])')]
        .find((b) => !b.closest('[hidden]') && /^\s*\+?\s*add\b/i.test(b.textContent ?? ''));
      if (add) return add;
      if (scope.matches('form, main, [role="dialog"], .card')) break;
    }
    return null;
  }

  /** The control on a sibling row that does what the lost one did: the same accessible label, else the same text. */
  private twin(row: Element | null, lost: HTMLElement): HTMLElement | null {
    if (!row) return null;
    const label = lost.getAttribute('aria-label');
    const text = lost.textContent?.trim() ?? '';
    const candidates = [...row.querySelectorAll<HTMLElement>(FOCUSABLE)];
    if (row instanceof HTMLElement && row.matches(FOCUSABLE)) candidates.unshift(row);
    return candidates.find((x) => label !== null && x.getAttribute('aria-label') === label)
      ?? candidates.find((x) => text !== '' && x.tagName === lost.tagName && x.textContent?.trim() === text)
      ?? null;
  }

  /** The first visible control after where the removed one sat, in document order. */
  private after(record: MutationRecord): HTMLElement | null {
    const next = nextElement(record.nextSibling);
    return this.focusables().find((x) =>
      next
        ? next === x || next.contains(x) || follows(next, x)
        : follows(record.target, x) && !record.target.contains(x)) ?? null;
  }

  /** The last visible control before where the removed one sat. */
  private before(record: MutationRecord): HTMLElement | null {
    const previous = previousElement(record.previousSibling);
    return this.focusables().filter((x) =>
      previous
        ? previous === x || previous.contains(x) || follows(x, previous)
        : follows(x, record.target)).at(-1) ?? null;
  }

  private focusables(): HTMLElement[] {
    return [...this.root!.querySelectorAll<HTMLElement>(FOCUSABLE)].filter((x) => !x.closest('[hidden]'));
  }
}

/** Skips Angular's comment anchors and whitespace to the nearest element at or after a node. */
function nextElement(node: Node | null): Element | null {
  let current = node;
  while (current && !(current instanceof Element)) current = current.nextSibling;
  return current;
}

function previousElement(node: Node | null): Element | null {
  let current = node;
  while (current && !(current instanceof Element)) current = current.previousSibling;
  return current;
}

/** Whether `b` comes after `a` in document order (and is not inside it). */
function follows(a: Node, b: Node): boolean {
  return (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0 && !a.contains(b);
}
