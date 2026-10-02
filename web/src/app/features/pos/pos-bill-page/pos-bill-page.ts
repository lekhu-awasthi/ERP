import { Component, Injector, afterNextRender, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';

import { extractErrorMessage, extractWarningKind } from '../../../core/auth/api-error';
import { fromPaisa, toPaisa } from '../../../core/pos/pos-bill';
import { POS_TAB_LABELS, PosReceipt, PosTill, PosTillPaymentMode } from '../../../core/pos/pos.models';
import {
  CreatePosOrderInvoiceResult,
  PosOrder,
  PosOrderBillPreview,
  PosOrderBillRequest,
  PosOrderLine,
  PosOrderSplit,
  PosRestaurant,
} from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { PosService } from '../../../core/pos/pos.service';
import { FieldError, FieldErrorMessage } from '../../../shared/a11y/field-error';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { PosReceiptView } from '../pos-receipt/pos-receipt';

/** One tender, in whole paisa so adding them up cannot drift (the till's arrangement). */
interface TenderRow {
  key: number;
  mode: PosTillPaymentMode;
  paisa: number;
}

/** What the cashier has chosen of one line for a split by item. */
interface ItemChoice {
  checked: boolean;
  quantity: number;
}

/** A confirmable 422 from the bill (phase 31's shape). */
interface BillWarning {
  kind: 'StockAvailability' | 'CreditLimit';
  message: string;
}

/**
 * Phase 65 -- billing a restaurant order: choose the part (all of it, chosen items and quantities, or
 * one of N equal parts), see what it comes to, take payment, print the tax invoice. Each payment is
 * one Invoice through the till's sale engine, in the cashier's open drawer.
 *
 * <p><b>Every figure is the server's</b>: the screen asks the bill planner for each choice and shows
 * its answer, and pays against that. A split's figure depends on the order's other bills -- the last
 * of a line takes exactly what is left, and parts round on the running total -- so a TypeScript copy
 * would agree with the server only until the second part (phase 63's lesson).</p>
 *
 * <p><b>The server keeps no split state.</b> An equal split asks for "one of N parts of what is left";
 * after a part is paid this screen counts N down, and the last part is simply what is left.</p>
 *
 * <p>Taking payment needs the cashier's own open session at this location; a waiter without one is
 * told so before anything is chosen, with the way to start one.</p>
 */
@Component({
  selector: 'app-pos-bill-page',
  imports: [RouterLink, StatusBanner, AmountPipe, FieldErrorMessage, PosReceiptView],
  templateUrl: './pos-bill-page.html',
  styleUrl: './pos-bill-page.scss',
})
export class PosBillPage {
  private readonly route = inject(ActivatedRoute);
  private readonly restaurantService = inject(PosRestaurantService);
  private readonly posService = inject(PosService);
  private readonly injector = inject(Injector);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly orderId = this.route.snapshot.paramMap.get('orderId')!;
  protected readonly tabLabels = POS_TAB_LABELS;
  protected readonly fromPaisa = fromPaisa;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly announcement = signal('');

  protected readonly order = signal<PosOrder | null>(null);
  protected readonly restaurant = signal<PosRestaurant | null>(null);
  protected readonly till = signal<PosTill | null>(null);

  // ---- The part ----
  protected readonly split = signal<PosOrderSplit>('Whole');
  protected readonly choices = signal<Record<string, ItemChoice>>({});
  protected readonly parts = signal(2);
  protected readonly preview = signal<PosOrderBillPreview | null>(null);
  protected readonly previewError = signal<string | null>(null);
  protected readonly previewing = signal(false);
  private previewTimer: ReturnType<typeof setTimeout> | null = null;
  private previewSeq = 0;

  // ---- Payment ----
  protected readonly tenders = signal<TenderRow[]>([]);
  protected readonly selectedModeId = signal<string | null>(null);
  protected readonly amountText = signal('');
  protected readonly payError = signal<string | null>(null);
  protected readonly warning = signal<BillWarning | null>(null);
  protected readonly submitting = signal(false);
  protected readonly fieldError = new FieldError(this.payError);
  private overrideStock = false;
  private overrideCredit = false;
  private nextTenderKey = 1;

  /** The bill just paid, and the receipt the print root shows. */
  protected readonly lastBill = signal<CreatePosOrderInvoiceResult | null>(null);
  protected readonly printing = signal<PosReceipt | null>(null);

  /** Lines with something still to bill, in order. */
  protected readonly billable = computed(() => (this.order()?.lines ?? []).filter((x) => x.toBill > 0));

  protected readonly totalPaisa = computed(() => toPaisa(this.preview()?.total ?? 0));
  protected readonly tenderedPaisa = computed(() => this.tenders().reduce((sum, x) => sum + x.paisa, 0));
  protected readonly cashPaisa = computed(() =>
    this.tenders().filter((x) => x.mode.kind === 'Cash').reduce((sum, x) => sum + x.paisa, 0));
  protected readonly remainingPaisa = computed(() => Math.max(0, this.totalPaisa() - this.tenderedPaisa()));
  protected readonly changePaisa = computed(() => Math.max(0, this.tenderedPaisa() - this.totalPaisa()));

  /** Phase 61 Decision C: change only ever comes out of cash that was handed over. */
  protected readonly changeError = computed(() =>
    this.changePaisa() > this.cashPaisa()
      ? 'Only cash gives change: a card or e-payment cannot be taken for more than the bill.'
      : null);

  /** A bill is paid in full here; leaving part of it on an account is the ERP's or the Retail till's. */
  protected readonly canPay = computed(
    () =>
      this.preview() !== null &&
      this.previewError() === null &&
      !this.previewing() &&
      !this.submitting() &&
      this.restaurant()?.mySessionId != null &&
      this.tenders().length > 0 &&
      this.remainingPaisa() === 0 &&
      this.changeError() === null,
  );

  protected readonly selectedMode = computed(
    () => this.till()?.paymentModes.find((x) => x.id === this.selectedModeId()) ?? null,
  );

  constructor() {
    this.load();
  }

  // ---- Loading ----

  private load(): void {
    this.loading.set(true);
    this.restaurantService
      .getOrder(this.organizationId, this.orderId)
      .pipe(
        switchMap((order) => {
          this.order.set(order);
          this.parts.set(Math.max(2, Math.min(order.covers, 20)));
          this.resetChoices(order);
          return this.restaurantService.getRestaurant(this.organizationId, order.locationId);
        }),
      )
      .subscribe({
        next: (restaurant) => {
          this.restaurant.set(restaurant);
          if (!restaurant.mySessionId) {
            this.loading.set(false);
            return;
          }
          this.posService.getTill(this.organizationId, restaurant.locationId).subscribe({
            next: (till) => {
              this.till.set(till);
              this.selectedModeId.set(till.paymentModes[0]?.id ?? null);
              this.loading.set(false);
              this.refreshPreview();
            },
            error: (err: unknown) => this.fail(err, 'Could not read the till.'),
          });
        },
        error: (err: unknown) => this.fail(err, 'Could not open the order.'),
      });
  }

  private fail(err: unknown, fallback: string): void {
    this.loading.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }

  private resetChoices(order: PosOrder): void {
    const choices: Record<string, ItemChoice> = {};
    for (const line of order.lines) {
      if (line.toBill > 0) choices[line.id] = { checked: false, quantity: line.toBill };
    }
    this.choices.set(choices);
  }

  // ---- Choosing the part ----

  protected chooseSplit(split: PosOrderSplit): void {
    this.split.set(split);
    this.schedulePreview();
  }

  protected isChecked(line: PosOrderLine): boolean {
    return this.choices()[line.id]?.checked ?? false;
  }

  protected quantityOf(line: PosOrderLine): number {
    return this.choices()[line.id]?.quantity ?? line.toBill;
  }

  protected toggleItem(line: PosOrderLine, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.choices.set({ ...this.choices(), [line.id]: { checked, quantity: this.quantityOf(line) } });
    this.schedulePreview();
  }

  protected setItemQuantity(line: PosOrderLine, event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    const quantity = Number.isFinite(value) ? Math.max(0, Math.min(value, line.toBill)) : 0;
    this.choices.set({ ...this.choices(), [line.id]: { checked: quantity > 0, quantity } });
    this.schedulePreview();
  }

  protected setParts(event: Event): void {
    const value = Math.trunc(Number((event.target as HTMLInputElement).value));
    this.parts.set(Number.isFinite(value) ? Math.max(1, Math.min(value, 100)) : 2);
    this.schedulePreview();
  }

  protected request(): PosOrderBillRequest {
    switch (this.split()) {
      case 'Items':
        return {
          split: 'Items',
          items: Object.entries(this.choices())
            .filter(([, c]) => c.checked && c.quantity > 0)
            .map(([lineId, c]) => ({ lineId, quantity: c.quantity })),
          parts: null,
        };
      case 'Equal':
        return { split: 'Equal', items: [], parts: this.parts() };
      default:
        return { split: 'Whole', items: [], parts: null };
    }
  }

  private schedulePreview(): void {
    if (this.previewTimer) clearTimeout(this.previewTimer);
    this.previewing.set(true);
    this.previewTimer = setTimeout(() => this.refreshPreview(), 250);
  }

  /** Asks the server what this part comes to; the latest answer wins. */
  protected refreshPreview(): void {
    const request = this.request();
    if (request.split === 'Items' && request.items.length === 0) {
      this.preview.set(null);
      this.previewing.set(false);
      this.previewError.set('Tick what goes on this bill.');
      return;
    }

    const seq = ++this.previewSeq;
    this.previewing.set(true);
    this.restaurantService.previewBill(this.organizationId, this.orderId, request).subscribe({
      next: (preview) => {
        if (seq !== this.previewSeq) return;
        this.preview.set(preview);
        this.previewError.set(null);
        this.previewing.set(false);
        this.tenders.set([]);
        this.amountText.set(preview.total.toFixed(2));
      },
      error: (err: unknown) => {
        if (seq !== this.previewSeq) return;
        this.preview.set(null);
        this.previewing.set(false);
        this.previewError.set(extractErrorMessage(err) ?? 'Could not price this bill.');
      },
    });
  }

  // ---- Payment ----

  protected chooseMode(modeId: string): void {
    this.selectedModeId.set(modeId);
  }

  protected onAmountInput(event: Event): void {
    this.amountText.set((event.target as HTMLInputElement).value);
  }

  protected addTender(): void {
    if (this.amountText().trim() === '' && this.canPay()) {
      this.pay();
      return;
    }

    const mode = this.selectedMode();
    const amount = Number(this.amountText());
    if (!mode) {
      this.payError.set('Choose how the guest is paying.');
      return;
    }
    if (!Number.isFinite(amount) || amount <= 0) {
      this.fieldError.fail('pos-bill-amount', 'Enter the amount handed over.');
      return;
    }

    this.payError.set(null);
    this.tenders.set([...this.tenders(), { key: this.nextTenderKey++, mode, paisa: toPaisa(amount) }]);
    this.amountText.set(this.remainingPaisa() > 0 ? fromPaisa(this.remainingPaisa()).toFixed(2) : '');
    this.announcement.set(`${mode.name} ${amount.toFixed(2)} added.`);
  }

  protected removeTender(key: number): void {
    this.tenders.set(this.tenders().filter((x) => x.key !== key));
    this.amountText.set(fromPaisa(this.remainingPaisa()).toFixed(2));
  }

  protected confirmWarning(): void {
    const warning = this.warning();
    if (!warning) return;
    if (warning.kind === 'StockAvailability') this.overrideStock = true;
    if (warning.kind === 'CreditLimit') this.overrideCredit = true;
    this.warning.set(null);
    this.pay();
  }

  protected pay(): void {
    const restaurant = this.restaurant();
    if (!restaurant?.mySessionId || !this.canPay()) return;

    this.submitting.set(true);
    this.payError.set(null);
    const request = this.request();

    this.restaurantService
      .billOrder(this.organizationId, this.orderId, {
        ...request,
        sessionId: restaurant.mySessionId,
        locationId: restaurant.locationId,
        tenders: this.tenders().map((x) => ({ paymentModeId: x.mode.id, amount: fromPaisa(x.paisa) })),
        changeAmount: fromPaisa(this.changePaisa()),
        contactId: null,
        overrideStockWarning: this.overrideStock,
        overrideCreditLimitWarning: this.overrideCredit,
      })
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.overrideStock = false;
          this.overrideCredit = false;
          this.lastBill.set(result);
          this.tenders.set([]);
          this.announcement.set(
            `Bill ${result.code} paid, ${result.grandTotal.toFixed(2)}.`
              + (result.changeAmount > 0 ? ` Change ${result.changeAmount.toFixed(2)}.` : '')
              + (result.orderStatus === 'Settled' ? ' The order is settled and the table is free.' : ` ${result.orderToBill.toFixed(2)} still to bill.`),
          );
          if (request.split === 'Equal') this.parts.set(Math.max(1, this.parts() - 1));
          // The Pay button goes with the bill it paid; focus goes to what happened (phase 64's panel rule).
          afterNextRender(() => document.getElementById('pos-bill-done')?.focus(), { injector: this.injector });
          if (restaurant.printInvoice) this.print(result.id);
          this.reloadOrder();
        },
        error: (err: unknown) => {
          this.submitting.set(false);
          const kind = extractWarningKind(err);
          if (kind === 'StockAvailability' || kind === 'CreditLimit') {
            this.warning.set({ kind, message: extractErrorMessage(err) ?? 'This bill needs confirming.' });
            return;
          }
          this.payError.set(extractErrorMessage(err) ?? 'The bill was not recorded.');
        },
      });
  }

  private reloadOrder(): void {
    this.restaurantService.getOrder(this.organizationId, this.orderId).subscribe({
      next: (order) => {
        this.order.set(order);
        this.resetChoices(order);
        if (order.status === 'Open' && order.toBill > 0) {
          if (this.split() === 'Items') this.split.set('Whole');
          this.refreshPreview();
        } else {
          this.preview.set(null);
        }
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not read the order again.'),
    });
  }

  /** Every call is a printing the server counts: the first is the original, any later one a copy. */
  protected print(invoiceId: string): void {
    this.posService.printReceipt(this.organizationId, invoiceId).subscribe({
      next: (receipt) => {
        this.printing.set(receipt);
        afterNextRender(() => window.print(), { injector: this.injector });
      },
      error: (err: unknown) => this.payError.set(extractErrorMessage(err) ?? 'Could not print the bill.'),
    });
  }
}
