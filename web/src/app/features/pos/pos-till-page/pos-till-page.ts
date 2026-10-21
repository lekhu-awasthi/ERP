import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage, extractWarningKind } from '../../../core/auth/api-error';
import { Contact } from '../../../core/contacts/contacts.models';
import { fromPaisa, toPaisa } from '../../../core/pos/pos-bill';
import { CartLine, PosCart } from '../../../core/pos/pos-cart';
import {
  CreatePosSaleResult,
  POS_TAB_LABELS,
  PosDiscountKind,
  PosProduct,
  PosProductBatch,
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
import { PosCustomerSearch } from '../../../shared/pos/pos-customer-search';
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
  imports: [RouterLink, StatusBanner, FieldErrorMessage, AmountPipe, NepaliDatePipe, PosReceiptView, PosCustomerSearch],
  templateUrl: './pos-till-page.html',
  styleUrl: './pos-till-page.scss',
  host: { '(document:keydown)': 'onShortcut($event)' },
})
export class PosTillPage {
  private readonly route = inject(ActivatedRoute);
  private readonly posService = inject(PosService);
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
  /** Phase 91 -- the open serial line's units in stock here (the picker), and what went wrong naming one. */
  protected readonly serialOptions = signal<string[]>([]);
  protected readonly serialError = signal<string | null>(null);
  /** Phase 94 -- the open batch line's batches in stock here (the picker), and why they could not be read. */
  protected readonly batchOptions = signal<PosProductBatch[]>([]);
  protected readonly batchError = signal<string | null>(null);
  protected readonly holdsOpen = signal(false);
  protected readonly customerOpen = signal(false);

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

  /**
   * Phase 72 -- why this sale cannot be rung up as it stands: a discount, or a line below its unit's catalogue
   * price, needs Sales.Invoice.Approve here, which the server checks too. Said in the cart and before Complete,
   * as credit is; the inputs stay usable, so a recalled cart can always be put back to the catalogue price.
   *
   * Phase 74 -- any price but the catalogue's, raised as well as lowered (the vendor's Can Change Rate), and an
   * amount discount like a percentage. And a bill discount of more than the bill is refused before it is sent.
   *
   * Phase 76 -- a discount needs Sales.Invoice.TillDiscount and any other price Sales.Invoice.TillChangeRate, no
   * longer Approve, which also approved every ERP draft at the branch.
   */
  protected readonly discountRefusal = computed(() => {
    const cart = this.cart();
    const till = this.till();
    if (!cart) return null;
    if (cart.discountAmount() > cart.bill().billDiscount) {
      return `A discount of ${cart.discountAmount().toFixed(2)} is more than the bill comes to.`;
    }
    // Phase 76 -- two keys, the vendor's two switches; the message names whichever is missing, or both.
    const lines = cart.lines();
    const discounted = cart.discountPct() > 0 || cart.discountAmount() > 0
      || lines.some((line) => line.discountPct > 0 || line.discountPerUnit > 0);
    const repriced = lines.some((line) => {
      const catalogue = line.product.units.find((unit) => unit.unitId === line.unitId)?.rate;
      // Phase 95 -- a priced batch's rate is the batch's price, not a changed one (the server keeps it the same way).
      return catalogue !== undefined && line.rate !== catalogue && line.rate !== cart.batchRateOf(line);
    });
    const missing = [
      ...(discounted && !till?.canDiscount ? ['a discount needs Sales.Invoice.TillDiscount'] : []),
      ...(repriced && !till?.canChangeRate ? ['a price other than the catalogue\'s needs Sales.Invoice.TillChangeRate'] : []),
    ];
    return missing.length > 0 ? `At this location, ${missing.join(', and ')}.` : null;
  });

  /** Phase 74 -- which kind each line's discount input takes, chosen by the cashier; until then, what the line has. A
   * signal of its own, because the line cannot say "Amount" while its amount is still zero (zoneless, phase 17). */
  private readonly lineDiscountKinds = signal<Record<number, PosDiscountKind>>({});
  private readonly chosenBillDiscountKind = signal<PosDiscountKind | null>(null);

  protected readonly billDiscountKind = computed<PosDiscountKind>(
    () => this.chosenBillDiscountKind() ?? ((this.cart()?.discountAmount() ?? 0) > 0 ? 'Amount' : 'Percent'),
  );

  protected lineDiscountKind(line: CartLine): PosDiscountKind {
    return this.lineDiscountKinds()[line.key] ?? (line.discountPerUnit > 0 ? 'Amount' : 'Percent');
  }

  /** A change of kind starts the discount again at nothing: 10% is not Rs 10 each. */
  protected onLineDiscountKind(line: CartLine, event: Event): void {
    const kind = (event.target as HTMLSelectElement).value as PosDiscountKind;
    this.lineDiscountKinds.update((kinds) => ({ ...kinds, [line.key]: kind }));
    this.cart()?.setLineDiscount(line.key, 0);
  }

  protected onLineDiscount(line: CartLine, event: Event): void {
    const value = this.numberOf(event);
    if (this.lineDiscountKind(line) === 'Amount') {
      this.cart()?.setLineDiscountPerUnit(line.key, value);
    } else {
      this.cart()?.setLineDiscount(line.key, value);
    }
  }

  protected onBillDiscountKind(event: Event): void {
    this.chosenBillDiscountKind.set((event.target as HTMLSelectElement).value as PosDiscountKind);
    this.cart()?.setBillDiscount(0);
  }

  protected onBillDiscount(event: Event): void {
    const value = this.numberOf(event);
    if (this.billDiscountKind() === 'Amount') {
      this.cart()?.setBillDiscountAmount(value);
    } else {
      this.cart()?.setBillDiscount(value);
    }
  }

  /** Phase 91 -- why the sale cannot be taken yet: a serial-tracked line names fewer (or more) units than it sells. */
  protected readonly serialRefusal = computed(() => {
    const shortfall = this.cart()?.serialShortfall();
    if (!shortfall) return null;
    const { line, named } = shortfall;
    return `${line.product.name} is serial-tracked: ${named} of ${line.quantity} serial number(s) named. Name each unit handed over.`;
  });

  /** Phase 94 -- why the sale cannot be taken yet: a batch-tracked line has no batch chosen (the vendor's till asks the
   * same: "Select a batch to continue"), which the server would refuse with a 400 naming the line. */
  protected readonly batchRefusal = computed(() => {
    const line = this.cart()?.unbatchedLine();
    return line ? `${line.product.name} is batch-tracked: choose the batch it is sold from.` : null;
  });

  protected readonly canComplete = computed(
    () =>
      (this.cart()?.lines().length ?? 0) > 0 &&
      !this.submitting() &&
      this.changeError() === null &&
      this.discountRefusal() === null &&
      this.serialRefusal() === null &&
      this.batchRefusal() === null &&
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

    // Phase 91 -- a serial-tracked product sells unit by unit: a scanned serial names the unit, and a tap opens the
    // line's picker so the cashier names it (the vendor's till asks the same, by scan or typing only).
    if (product.serialTracking) {
      const scanned = product.scannedSerialNo;
      if (scanned) {
        if (cart.addSerial(product, scanned)) {
          this.announce(`Added ${product.name}, serial ${scanned}. Total ${cart.bill().grandTotal.toFixed(2)}.`);
        } else {
          this.noticeMessage.set(`Serial ${scanned} is already in the cart.`);
        }
        return;
      }

      const line = cart.add(product);
      if (line) this.openSerials(line);
      this.announce(`Added ${product.name}. Name its serial number.`);
      return;
    }

    // Phase 94 -- a batch-tracked product is sold from a batch: the line opens on its picker until one is chosen.
    if (product.batchTracking) {
      const line = cart.add(product);
      if (line) this.openBatches(line);
      this.announce(`Added ${product.name}. Choose its batch.`);
      return;
    }

    cart.add(product);
    this.announce(`Added ${product.name}. ${cart.lines().length} line(s), total ${cart.bill().grandTotal.toFixed(2)}.`);
  }

  // ---- Batches (phase 94) ----

  /** Opens a batch line's edit panel on its picker, reading what is in stock here, and puts the cursor on it. */
  private openBatches(line: CartLine): void {
    this.editingKey.set(line.key);
    this.loadBatches(line, true);
  }

  private loadBatches(line: CartLine, focus = false): void {
    const till = this.till();
    if (!till) return;
    this.batchError.set(null);
    this.batchOptions.set([]);
    this.posService.listProductBatches(this.organizationId, till.locationId, line.product.id).subscribe({
      next: (batches) => {
        this.batchOptions.set(batches);
        // The radio already chosen, or the first: a radio group takes focus on one of its own (phase 40).
        if (focus) this.focusById(this.batchRadioId(line, this.batchChoices(line).findIndex((x) => x.batchNo === line.batchNo)));
      },
      error: (err: unknown) => this.batchError.set(extractErrorMessage(err) ?? 'Could not read the batches in stock.'),
    });
  }

  /** The picker's rows: what is in stock here, and the line's own batch first when it is no longer among them (a
   * restored cart), so a choice is never shown as unmade. */
  protected batchChoices(line: CartLine): PosProductBatch[] {
    const options = this.batchOptions();
    const chosen = line.batchNo;
    return chosen && !options.some((x) => x.batchNo === chosen)
      ? [{ batchNo: chosen, expiryDate: null, quantity: 0 }, ...options]
      : options;
  }

  protected batchRadioId(line: CartLine, index: number): string {
    return index < 0 ? `pos-batch-${line.key}-0` : `pos-batch-${line.key}-${index}`;
  }

  protected onBatchPick(line: CartLine, batch: PosProductBatch): void {
    this.cart()?.setBatch(line.key, batch.batchNo, batch.rate ?? null);
    this.announce(`${line.product.name} from batch ${batch.batchNo}.`);
  }

  // ---- Serials (phase 91) ----

  /** Opens a serial line's edit panel on its picker, reading what is in stock here, and puts the cursor in its box. */
  private openSerials(line: CartLine): void {
    this.editingKey.set(line.key);
    this.serialError.set(null);
    this.loadSerials(line);
    this.focusById(`pos-serial-${line.key}`);
  }

  private loadSerials(line: CartLine): void {
    const till = this.till();
    if (!till) return;
    this.serialOptions.set([]);
    this.posService.listProductSerials(this.organizationId, till.locationId, line.product.id).subscribe({
      next: (serials) => this.serialOptions.set(serials),
      error: (err: unknown) => this.serialError.set(extractErrorMessage(err) ?? 'Could not read the serials in stock.'),
    });
  }

  /** The picker's rows: the units named on the line first, then those in stock here not yet named. */
  protected serialChoices(line: CartLine): string[] {
    const named = line.serialNumbers ?? [];
    return [...named, ...this.serialOptions().filter((x) => !named.some((n) => n.toLowerCase() === x.toLowerCase()))];
  }

  protected isNamed(line: CartLine, serial: string): boolean {
    return (line.serialNumbers ?? []).includes(serial);
  }

  protected onSerialToggle(line: CartLine, serial: string, event: Event): void {
    const cart = this.cart();
    if (!cart) return;
    if ((event.target as HTMLInputElement).checked) {
      cart.addSerial(line.product, serial);
      this.announce(`Serial ${serial} added to ${line.product.name}.`);
    } else {
      cart.removeSerial(line.key, serial);
      this.announce(`Serial ${serial} taken off ${line.product.name}.`);
    }
  }

  /**
   * Enter in the line's serial box: a scanner or a typed serial. It is checked against what is in stock here before it
   * is named, so a typo is said at the counter and not as the server's refusal at payment.
   */
  protected onSerialEnter(line: CartLine, event: Event): void {
    event.preventDefault();
    const input = event.target as HTMLInputElement;
    const typed = input.value.trim();
    const cart = this.cart();
    const till = this.till();
    if (!typed || !cart || !till) return;

    if (cart.hasSerial(typed)) {
      this.serialError.set(`Serial ${typed} is already in the cart.`);
      return;
    }

    this.posService.listProductSerials(this.organizationId, till.locationId, line.product.id, typed).subscribe({
      next: (serials) => {
        const match = serials.find((x) => x.toLowerCase() === typed.toLowerCase());
        if (!match) {
          this.serialError.set(`${typed} is not a serial of ${line.product.name} in stock at this till.`);
          return;
        }
        cart.addSerial(line.product, match);
        this.serialError.set(null);
        input.value = '';
        this.announce(`Serial ${match} added to ${line.product.name}.`);
      },
      error: (err: unknown) => this.serialError.set(extractErrorMessage(err) ?? 'Could not check that serial.'),
    });
  }

  // ---- Cart ----

  protected lineFigures(index: number) {
    return this.cart()?.bill().lines[index];
  }

  protected unitName(line: CartLine): string {
    return line.product.units.find((u) => u.unitId === line.unitId)?.shortName ?? '';
  }

  protected toggleEdit(key: number): void {
    const opening = this.editingKey() !== key;
    this.editingKey.set(opening ? key : null);
    // Phase 91 -- a serial line's panel is its picker, read fresh each time it opens.
    const line = this.cart()?.lines().find((x) => x.key === key);
    if (opening && line?.product.serialTracking) {
      this.serialError.set(null);
      this.loadSerials(line);
    }
    // Phase 94 -- and a batch line's, the same way.
    if (opening && line?.product.batchTracking) {
      this.loadBatches(line);
    }
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

  protected chooseCustomer(contact: Contact | null): void {
    this.cart()?.setCustomer(contact ? { id: contact.id, code: contact.code, name: contact.name } : null);
    this.customerOpen.set(false);
    // Phase 72 -- Use Walk-in removes the button just pressed; focus stays on the customer controls (a chosen
    // customer's return is the search component's own, through returnFocusTo).
    if (!contact) {
      afterNextRender(() => document.getElementById('pos-customer-toggle')?.focus(), { injector: this.injector });
    }
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

  /**
   * Phase 85 -- its own button goes with it, so focus goes to the amount field, prefilled with what is now left to pay
   * (phase 84's refund screen, the same shape): the keyboard is never dropped onto the page.
   */
  protected removeTender(key: number): void {
    const removed = this.tenders().find((x) => x.key === key);
    this.tenders.set(this.tenders().filter((x) => x.key !== key));
    this.amountText.set(fromPaisa(this.remainingPaisa()).toFixed(2));
    if (removed) {
      this.announce(`${removed.mode.name} ${fromPaisa(removed.paisa).toFixed(2)} removed.`);
    }
    this.focusIn('#pos-tender-amount');
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
        discountAmount: cart.discountAmount(),
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

  private focusById(id: string): void {
    afterNextRender(() => document.getElementById(id)?.focus(), { injector: this.injector });
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
