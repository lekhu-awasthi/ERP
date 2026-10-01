import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage, extractWarningKind } from '../../../core/auth/api-error';
import { Contact } from '../../../core/contacts/contacts.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { fromPaisa, toPaisa } from '../../../core/pos/pos-bill';
import { CartLine, PosCart } from '../../../core/pos/pos-cart';
import {
  CreatePosSaleResult,
  POS_TAB_LABELS,
  PosProduct,
  PosReceipt,
  PosSession,
  PosTab,
  PosTill,
  PosTillPaymentMode,
} from '../../../core/pos/pos.models';
import { PosService } from '../../../core/pos/pos.service';
import { FieldError, FieldErrorMessage } from '../../../shared/a11y/field-error';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { PosReceiptView } from '../pos-receipt/pos-receipt';

/** One tender on the payment screen, in whole paisa so adding them up cannot drift. */
interface TenderRow {
  key: number;
  mode: PosTillPaymentMode;
  paisa: number;
}

/** A confirmable 422 from the sale (phase 31's shape), with the override it asks for. */
interface SaleWarning {
  kind: 'StockAvailability' | 'CreditLimit';
  message: string;
}

const PAGE_SIZE = 48;

/**
 * Phase 62 -- the Retail till: product grid, cart, payment and receipt on one full-screen page
 * (the shell drops its chrome under `/pos`). Everything it sells goes through phase 61's
 * `POST /pos/sales`; nothing here posts on its own.
 *
 * <p><b>The total on screen is the server's total.</b> `PosCart.bill` computes it with the same
 * rounding `InvoiceLine.CreatePos` uses, pinned to one shared table (pos-bill.ts), because the
 * cashier takes money against it before the server has seen the sale.</p>
 *
 * <p><b>Credit is said before Pay, not after.</b> The till reads whether the cashier may leave
 * anything on credit (`canSellOnCredit`) and whether the customer is the walk-in, and the payment
 * screen explains a refusal rather than letting the server turn the customer away.</p>
 *
 * <p><b>Keyboard</b> (phase 40's census): the search box is the scanner's input and has focus when
 * the till opens; F2 returns to it and F9 opens payment. The payment screen is a modal dialog that
 * keeps focus inside it, closes on Escape, and hands focus back to Pay.</p>
 */
@Component({
  selector: 'app-pos-till-page',
  imports: [RouterLink, StatusBanner, FieldErrorMessage, AmountPipe, NepaliDatePipe, PosReceiptView],
  templateUrl: './pos-till-page.html',
  styleUrl: './pos-till-page.scss',
  host: { '(document:keydown)': 'onShortcut($event)' },
})
export class PosTillPage {
  private readonly route = inject(ActivatedRoute);
  private readonly posService = inject(PosService);
  private readonly contactsService = inject(ContactsService);
  private readonly injector = inject(Injector);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly tabLabels = POS_TAB_LABELS;

  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');
  private readonly payButton = viewChild<ElementRef<HTMLButtonElement>>('payButton');
  private readonly dialog = viewChild<ElementRef<HTMLElement>>('dialog');

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly noticeMessage = signal<string | null>(null);
  /** Polite, visually hidden: what a scan or a tap just did, for a screen reader. */
  protected readonly announcement = signal('');

  protected readonly till = signal<PosTill | null>(null);
  protected readonly session = signal<PosSession | null>(null);
  protected readonly cart = signal<PosCart | null>(null);

  // ---- The grid ----
  protected readonly products = signal<PosProduct[]>([]);
  protected readonly productTotal = signal(0);
  protected readonly page = signal(1);
  protected readonly categoryId = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.productTotal() / PAGE_SIZE)));

  // ---- The cart panel ----
  protected readonly editingKey = signal<number | null>(null);
  protected readonly holdsOpen = signal(false);
  protected readonly customerOpen = signal(false);
  protected readonly customerResults = signal<Contact[]>([]);
  private customerTimer: ReturnType<typeof setTimeout> | null = null;

  // ---- Payment ----
  protected readonly paying = signal(false);
  protected readonly tenders = signal<TenderRow[]>([]);
  protected readonly selectedModeId = signal<string | null>(null);
  protected readonly amountText = signal('');
  protected readonly creditAccepted = signal(false);
  protected readonly submitting = signal(false);
  protected readonly payError = signal<string | null>(null);
  /** Phase 47's shape, on the payment screen's own message. */
  protected readonly fieldError = new FieldError(this.payError);
  protected readonly warning = signal<SaleWarning | null>(null);
  private overrideStock = false;
  private overrideCredit = false;
  private nextTenderKey = 1;

  protected readonly sale = signal<CreatePosSaleResult | null>(null);
  protected readonly receipt = signal<PosReceipt | null>(null);
  /** What the print root renders; set just before `window.print()`. */
  protected readonly printing = signal<PosReceipt | null>(null);

  protected readonly totalPaisa = computed(() => toPaisa(this.cart()?.bill().grandTotal ?? 0));
  protected readonly tenderedPaisa = computed(() => this.tenders().reduce((sum, x) => sum + x.paisa, 0));
  private readonly cashPaisa = computed(() =>
    this.tenders().filter((x) => x.mode.kind === 'Cash').reduce((sum, x) => sum + x.paisa, 0));
  protected readonly remainingPaisa = computed(() => Math.max(0, this.totalPaisa() - this.tenderedPaisa()));
  protected readonly changePaisa = computed(() => Math.max(0, this.tenderedPaisa() - this.totalPaisa()));

  /** Phase 61 Decision C: change only ever comes out of cash that was handed over. */
  protected readonly changeError = computed(() =>
    this.changePaisa() > this.cashPaisa()
      ? 'Only cash gives change: a card or e-payment cannot be taken for more than the bill. Lower it, or take the difference in cash.'
      : null);

  /** Why the remainder cannot go on credit, or null when it can (phase 61 Decisions F and G). */
  protected readonly creditRefusal = computed(() => {
    if (this.remainingPaisa() === 0) return null;
    if (!this.cart()?.customer()) {
      return 'The walk-in customer cannot take credit. Take the rest of the bill, or choose a named customer.';
    }
    if (!this.till()?.canSellOnCredit) {
      return 'Leaving part of a bill on credit needs permission to approve invoices at this location (Sales.Invoice.Approve).';
    }
    return null;
  });

  protected readonly canComplete = computed(
    () =>
      (this.cart()?.lines().length ?? 0) > 0 &&
      !this.submitting() &&
      this.changeError() === null &&
      (this.remainingPaisa() === 0 || (this.creditRefusal() === null && this.creditAccepted())),
  );

  protected readonly selectedMode = computed(
    () => this.till()?.paymentModes.find((x) => x.id === this.selectedModeId()) ?? null,
  );

  /** Notes a cash customer is likely to hand over: the next 100, 500 and 1000 at or above what is owed. */
  protected readonly quickCash = computed(() => {
    const owed = fromPaisa(this.remainingPaisa());
    if (owed <= 0) return [];
    const options = [100, 500, 1000].map((step) => Math.ceil(owed / step) * step);
    return [...new Set([owed, ...options])].sort((a, b) => a - b);
  });

  constructor() {
    this.route.paramMap.subscribe((params) => this.load(params.get('locationId')!));
  }

  // ---- Loading ----

  private load(locationId: string): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.posService.getTill(this.organizationId, locationId).subscribe({
      next: (till) => {
        this.till.set(till);
        this.posService.getMySession(this.organizationId, locationId).subscribe({
          next: (session) => {
            this.session.set(session);
            this.cart.set(session ? new PosCart(till, this.organizationId, session.id) : null);
            this.loading.set(false);
            if (session) {
              this.loadProducts();
              this.focusSearch();
            }
          },
          error: (err: unknown) => this.fail(err, 'Could not read your session at this till.'),
        });
      },
      error: (err: unknown) => this.fail(err, 'Could not open this till.'),
    });
  }

  protected loadProducts(): void {
    const till = this.till();
    if (!till) return;

    this.posService
      .listProducts(this.organizationId, till.locationId, {
        search: this.searchTerm().trim() || undefined,
        categoryId: this.categoryId() ?? undefined,
        page: this.page(),
        pageSize: PAGE_SIZE,
      })
      .subscribe({
        next: (result) => {
          this.products.set(result.items);
          this.productTotal.set(result.totalCount);
        },
        error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the products.'),
      });
  }

  // ---- Grid ----

  protected chooseCategory(categoryId: string | null): void {
    this.categoryId.set(categoryId);
    this.page.set(1);
    this.loadProducts();
  }

  protected goToPage(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.pageCount()));
    this.loadProducts();
  }

  protected onSearchInput(event: Event): void {
    this.searchTerm.set((event.target as HTMLInputElement).value);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.page.set(1);
      this.loadProducts();
    }, 250);
  }

  /**
   * Enter in the search box: what a barcode scanner sends after the code. An exact code adds its one
   * product; several products sharing a barcode (allowed since phase 24) are shown to choose from,
   * never guessed between; a typed name that narrowed the grid to one product adds that one.
   */
  protected onScan(): void {
    const till = this.till();
    const term = this.searchTerm().trim();
    if (!till || !term) return;
    if (this.searchTimer) clearTimeout(this.searchTimer);

    this.posService.listProducts(this.organizationId, till.locationId, { code: term, pageSize: 10 }).subscribe({
      next: (result) => {
        if (result.items.length === 1) {
          this.addProduct(result.items[0]);
          this.resetSearch();
          return;
        }

        if (result.items.length > 1) {
          this.products.set(result.items);
          this.productTotal.set(result.items.length);
          this.announce(`${result.items.length} products share the code ${term}. Choose one.`);
          return;
        }

        // Not a code: fall back to the typed search, and add its match when there is exactly one.
        this.posService.listProducts(this.organizationId, till.locationId, { search: term, pageSize: 2 }).subscribe({
          next: (search) => {
            if (search.items.length === 1) {
              this.addProduct(search.items[0]);
              this.resetSearch();
            } else {
              this.page.set(1);
              this.loadProducts();
              this.announce(search.items.length === 0 ? `Nothing matches ${term}.` : `Several products match ${term}.`);
            }
          },
        });
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not look the code up.'),
    });
  }

  private resetSearch(): void {
    const box = this.searchBox()?.nativeElement;
    if (box) box.value = '';
    this.searchTerm.set('');
    this.page.set(1);
    this.loadProducts();
  }

  protected addProduct(product: PosProduct): void {
    const cart = this.cart();
    if (!cart) return;

    cart.add(product);
    this.announce(`Added ${product.name}. ${cart.lines().length} line(s), total ${cart.bill().grandTotal.toFixed(2)}.`);
  }

  // ---- Cart ----

  protected lineFigures(index: number) {
    return this.cart()?.bill().lines[index];
  }

  protected unitName(line: CartLine): string {
    return line.product.units.find((u) => u.unitId === line.unitId)?.shortName ?? '';
  }

  protected toggleEdit(key: number): void {
    this.editingKey.set(this.editingKey() === key ? null : key);
  }

  protected step(line: CartLine, delta: number): void {
    const next = line.quantity + delta;
    if (next <= 0) {
      this.removeLine(line);
      return;
    }
    this.cart()?.setQuantity(line.key, next);
  }

  protected removeLine(line: CartLine): void {
    this.cart()?.remove(line.key);
    this.announce(`Removed ${line.product.name}.`);
  }

  protected numberOf(event: Event): number {
    return (event.target as HTMLInputElement).valueAsNumber;
  }

  protected onUnitChange(line: CartLine, event: Event): void {
    this.cart()?.setUnit(line.key, (event.target as HTMLSelectElement).value);
  }

  protected setOrderType(tab: PosTab): void {
    this.cart()?.setOrderType(tab);
  }

  protected hold(): void {
    if (this.cart()?.hold()) {
      this.noticeMessage.set('The cart is held. Recall it from Held carts.');
      this.focusSearch();
    }
  }

  protected recall(id: string): void {
    this.cart()?.recall(id);
    this.holdsOpen.set(false);
    this.noticeMessage.set('Held cart recalled.');
  }

  protected clearCart(): void {
    this.cart()?.clear();
    this.focusSearch();
  }

  // ---- Customer ----

  protected onCustomerInput(event: Event): void {
    const term = (event.target as HTMLInputElement).value.trim();
    if (this.customerTimer) clearTimeout(this.customerTimer);
    if (!term) {
      this.customerResults.set([]);
      return;
    }

    this.customerTimer = setTimeout(() => {
      this.contactsService.listContacts(this.organizationId, 'Customer', 1, 8, { search: term }).subscribe({
        next: (result) => this.customerResults.set(result.items.filter((x) => x.id !== this.till()?.walkInCustomer?.id)),
        error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not search customers.'),
      });
    }, 250);
  }

  protected chooseCustomer(contact: Contact | null): void {
    this.cart()?.setCustomer(contact ? { id: contact.id, code: contact.code, name: contact.name } : null);
    this.customerOpen.set(false);
    this.customerResults.set([]);
  }

  // ---- Payment ----

  protected openPayment(): void {
    const cart = this.cart();
    if (!cart || cart.lines().length === 0 || this.paying()) return;

    this.tenders.set([]);
    this.creditAccepted.set(false);
    this.payError.set(null);
    this.warning.set(null);
    this.overrideStock = false;
    this.overrideCredit = false;
    this.sale.set(null);
    this.receipt.set(null);
    this.selectedModeId.set(this.till()?.paymentModes[0]?.id ?? null);
    this.amountText.set(fromPaisa(this.totalPaisa()).toFixed(2));
    this.paying.set(true);
    this.focusIn('#pos-tender-amount');
  }

  protected closePayment(): void {
    const finished = this.sale() !== null;
    this.paying.set(false);
    if (finished) {
      this.sale.set(null);
      this.receipt.set(null);
      this.focusSearch();
    } else {
      afterNextRender(() => this.payButton()?.nativeElement.focus(), { injector: this.injector });
    }
  }

  protected chooseMode(modeId: string): void {
    this.selectedModeId.set(modeId);
  }

  protected press(key: string): void {
    const text = this.amountText();
    if (key === 'back') {
      this.amountText.set(text.slice(0, -1));
    } else if (key === '.' && text.includes('.')) {
      return;
    } else {
      this.amountText.set(text + key);
    }
  }

  protected onAmountInput(event: Event): void {
    this.amountText.set((event.target as HTMLInputElement).value);
  }

  protected setAmount(value: number): void {
    this.amountText.set(value.toFixed(2));
  }

  protected addTender(): void {
    // Enter on an empty amount once the bill is covered completes the sale, so a keyboard-only cash
    // sale is: scan, F9, Enter (takes the amount due), Enter (completes).
    if (this.amountText().trim() === '' && this.canComplete()) {
      this.completeSale();
      return;
    }

    const mode = this.selectedMode();
    const amount = Number(this.amountText());
    if (!mode) {
      this.payError.set('Choose how the customer is paying.');
      return;
    }
    if (!Number.isFinite(amount) || amount <= 0) {
      this.fieldError.fail('pos-tender-amount', 'Enter the amount handed over.');
      return;
    }

    this.payError.set(null);
    this.tenders.set([...this.tenders(), { key: this.nextTenderKey++, mode, paisa: toPaisa(amount) }]);
    this.amountText.set(this.remainingPaisa() > 0 ? fromPaisa(this.remainingPaisa()).toFixed(2) : '');
    this.announce(`${mode.name} ${amount.toFixed(2)} added.`);
  }

  protected removeTender(key: number): void {
    this.tenders.set(this.tenders().filter((x) => x.key !== key));
  }

  protected onCreditToggle(event: Event): void {
    this.creditAccepted.set((event.target as HTMLInputElement).checked);
  }

  /** Confirms a 422 warning and sends the sale again with that warning's override. */
  protected confirmWarning(): void {
    const warning = this.warning();
    if (!warning) return;
    if (warning.kind === 'StockAvailability') this.overrideStock = true;
    if (warning.kind === 'CreditLimit') this.overrideCredit = true;
    this.warning.set(null);
    this.completeSale();
  }

  protected completeSale(): void {
    const cart = this.cart();
    const till = this.till();
    const session = this.session();
    if (!cart || !till || !session || !this.canComplete()) return;

    this.submitting.set(true);
    this.payError.set(null);

    this.posService
      .createSale(this.organizationId, {
        sessionId: session.id,
        locationId: till.locationId,
        lines: cart.toSaleLines(),
        tenders: this.tenders().map((x) => ({ paymentModeId: x.mode.id, amount: fromPaisa(x.paisa) })),
        changeAmount: this.remainingPaisa() === 0 ? fromPaisa(this.changePaisa()) : 0,
        contactId: cart.customer()?.id ?? null,
        warehouseId: null,
        orderType: cart.orderType(),
        discountPct: cart.discountPct(),
        overrideStockWarning: this.overrideStock,
        overrideCreditLimitWarning: this.overrideCredit,
      })
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.sale.set(result);
          cart.clear();
          this.announce(
            `Sale ${result.code} complete. ${result.changeAmount > 0 ? `Change ${result.changeAmount.toFixed(2)}.` : ''}`,
          );
          this.focusIn('#pos-new-sale');
          if (till.printInvoice) {
            this.print(result.id);
          }
        },
        error: (err: unknown) => {
          this.submitting.set(false);
          const kind = extractWarningKind(err);
          if (kind === 'StockAvailability' || kind === 'CreditLimit') {
            this.warning.set({ kind, message: extractErrorMessage(err) ?? 'This sale needs confirming.' });
            return;
          }
          this.payError.set(extractErrorMessage(err) ?? 'The sale was not recorded.');
        },
      });
  }

  /** Every call is a printing the server counts: the first is the original, any later one a copy. */
  protected print(invoiceId: string): void {
    this.posService.printReceipt(this.organizationId, invoiceId).subscribe({
      next: (receipt) => {
        this.receipt.set(receipt);
        this.printing.set(receipt);
        afterNextRender(() => window.print(), { injector: this.injector });
      },
      error: (err: unknown) => this.payError.set(extractErrorMessage(err) ?? 'Could not print the receipt.'),
    });
  }

  // ---- Keyboard ----

  protected onShortcut(event: KeyboardEvent): void {
    if (event.key === 'F2') {
      event.preventDefault();
      if (!this.paying()) this.focusSearch();
    } else if (event.key === 'F9') {
      event.preventDefault();
      this.openPayment();
    }
  }

  /** Escape closes the dialog; Tab and Shift+Tab wrap inside it (a modal keeps focus in itself). */
  protected onDialogKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closePayment();
      return;
    }

    if (event.key !== 'Tab') return;

    const root = this.dialog()?.nativeElement;
    if (!root) return;

    const focusable = [...root.querySelectorAll<HTMLElement>(
      'button:not([disabled]), input:not([disabled]), select:not([disabled]), a[href]',
    )];
    if (focusable.length === 0) return;

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  protected fromPaisa(paisa: number): number {
    return fromPaisa(paisa);
  }

  private focusSearch(): void {
    afterNextRender(() => this.searchBox()?.nativeElement.focus(), { injector: this.injector });
  }

  private focusIn(selector: string): void {
    afterNextRender(() => this.dialog()?.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }

  private announce(message: string): void {
    this.announcement.set(message);
  }

  private fail(err: unknown, fallback: string): void {
    this.loading.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }
}
