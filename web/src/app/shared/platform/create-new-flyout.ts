import { Component, ElementRef, HostListener, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Phase 34b — the global quick-create flyout, pinned above the left nav exactly as the reference
 * product pins it.
 *
 * <b>The one scan claim that survived its confirm-live intact.</b> `erp-module-scan.md` describes a
 * "4-column flyout: General / Sales / Purchase / Accounting" with nineteen items, and on 2026-09-10
 * the live panel was read out of the DOM and matched **verbatim** — the first time in several phases
 * that a recorded description of a control nobody had operated turned out to be exactly right
 * (32, 32b, 33 and 34a each had to correct one). Worth writing down in both directions: the rule
 * from phase-32b is that an unoperated description is *not settled*, not that it is wrong.
 *
 * <b>Why this list is written down when the nav's is not.</b> The nav is every screen, so deriving
 * it from the router is strictly better than listing it — a missing entry there is a screen nobody
 * can reach. This is a *curated* shortcut tray: nineteen of the forty-odd things this app can
 * create, chosen to match the reference. Deriving it would produce a different, longer panel and
 * lose the thing it is for. What the guard test enforces instead is that every url here resolves to
 * a real catalogue entry, so the list can be wrong about taste but never about routing.
 *
 * Driven from a signal rather than `data-bs-toggle`: Bootstrap's JavaScript is not loaded anywhere
 * in this app, so the attribute would do nothing (phase-22's gotcha), and nothing sets
 * `aria-expanded` for free either (phase-34a).
 */
@Component({
  selector: 'app-create-new-flyout',
  imports: [RouterLink],
  templateUrl: './create-new-flyout.html',
  styleUrl: './create-new-flyout.scss',
})
export class CreateNewFlyout {
  readonly organizationId = input.required<string>();

  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly open = signal(false);
  protected readonly columns = CREATE_NEW_COLUMNS;

  protected toggle(): void {
    this.open.update((x) => !x);
  }

  protected close(): void {
    this.open.set(false);
  }

  protected linkFor(url: string): unknown[] {
    return ['/organizations', this.organizationId(), ...url.split('/').filter((s) => s.length > 0)];
  }

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  /** WCAG 2.1.2 — a keyboard user must be able to leave the panel without tabbing through it. */
  @HostListener('keydown.escape')
  protected onEscape(): void {
    this.open.set(false);
  }
}

/** One shortcut in the flyout. */
export interface CreateNewLink {
  readonly label: string;
  /** A catalogue url — `create-new-flyout.spec.ts` fails if it resolves to no route. */
  readonly url: string;
}

/**
 * The panel's four columns, in the reference product's own order and wording where this codebase has
 * the same concept.
 *
 * **Three deliberate divergences**, each because the underlying model differs rather than the taste:
 * - *Customer* and *Supplier* are one **Contact** here — this codebase has a single Contacts screen
 *   with a type on the record, so two entries would open the same form.
 * - *Accounts* and *Accounts Group* have no `/new` route: both are created inline on their list
 *   screen, so these two open the list.
 * - *Quick Payment* and *Quick Receipt* are forms with no list, so their own url is the target.
 */
export const CREATE_NEW_COLUMNS: readonly { readonly heading: string; readonly links: readonly CreateNewLink[] }[] = [
  {
    heading: 'General',
    links: [
      { label: 'Contact', url: '/contacts/new' },
      { label: 'Product', url: '/products/new' },
      { label: 'Account', url: '/accounting/accounts' },
      { label: 'Account Group', url: '/accounting/account-groups' },
    ],
  },
  {
    heading: 'Sales',
    links: [
      { label: 'Quotation', url: '/sales/quotations/new' },
      { label: 'Sales Order', url: '/sales/sales-orders/new' },
      { label: 'Invoice', url: '/sales/invoices/new' },
      { label: 'Customer Payment', url: '/payments/new' },
      { label: 'Credit Note', url: '/sales/credit-notes/new' },
    ],
  },
  {
    heading: 'Purchase',
    links: [
      { label: 'Purchase Order', url: '/purchasing/purchase-orders/new' },
      { label: 'Purchase Bill', url: '/purchasing/purchase-bills/new' },
      { label: 'Expense', url: '/purchasing/expenses/new' },
      { label: 'Supplier Payment', url: '/purchasing/supplier-payments/new' },
      { label: 'Debit Note', url: '/purchasing/debit-notes/new' },
    ],
  },
  {
    heading: 'Accounting',
    links: [
      { label: 'Journal Voucher', url: '/accounting/journal-vouchers/new' },
      { label: 'Cash Transfer', url: '/accounting/cash-transfers/new' },
      { label: 'Quick Payment', url: '/quick-payment' },
      { label: 'Quick Receipt', url: '/quick-receipt' },
    ],
  },
];
