import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { fromPaisa, toPaisa } from '../../../core/pos/pos-bill';
import {
  CreatePosRefundResult,
  PosRefundPreview,
  PosRefundReceipt,
  PosRefundableLine,
  PosRefundableSale,
  PosSaleMatch,
  PosSession,
  PosTill,
} from '../../../core/pos/pos.models';
import { PosService } from '../../../core/pos/pos.service';
import { FieldError, FieldErrorMessage } from '../../../shared/a11y/field-error';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { PosRefundReceiptView } from '../pos-receipt/pos-refund-receipt';

/** The app's one money format (lakh/crore grouping), for messages built in code. */
const money = new AmountPipe();

/**
 * Phase 63 -- the refund screen: find a sale by its number (or arrive with one from the session page),
 * choose what came back, say why, and pay it back out of the drawer.
 *
 * <p><b>The figure is the server's.</b> Unlike a sale's total, a refund's depends on other documents
 * -- the sale's earlier refunds, and what the customer still owes on it -- so the screen keeps no copy
 * of the arithmetic: every change of quantity asks `POST /pos/refunds/preview`, which runs the refund's
 * own planner (phase-63-status.md Decision F), and the payout offered is exactly what it says must be
 * handed back (Decision D).</p>
 *
 * <p><b>Payout modes (phase 84).</b> Complete hands the whole of it back in the chosen mode, Cash first, as phase
 * 63 did. To split it, the cashier enters part of it and adds it, as the till takes tenders; once anything is
 * added, the parts must come to exactly what is owed back (the server's rule, Decision D) before Complete is
 * offered. A removed part hands focus back to the amount field, so the keyboard is never dropped.</p>
 *
 * <p><b>Another location's sale (phase 84).</b> Found by its full number; it is refunded here, out of this
 * drawer, and its goods go back into this till's warehouse, which the sale panel says.</p>
 */
@Component({
  selector: 'app-pos-refund-page',
  imports: [RouterLink, StatusBanner, FieldErrorMessage, AmountPipe, NepaliDatePipe, PosRefundReceiptView],
  templateUrl: './pos-refund-page.html',
})
export class PosRefundPage {
  private readonly route = inject(ActivatedRoute);
  private readonly posService = inject(PosService);
  private readonly injector = inject(Injector);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly fieldError = new FieldError(this.errorMessage);

  protected readonly till = signal<PosTill | null>(null);
  protected readonly session = signal<PosSession | null>(null);

  // ---- Finding the sale ----
  protected readonly searchTerm = signal('');
  protected readonly matches = signal<PosSaleMatch[]>([]);
  protected readonly searched = signal(false);

  // ---- The sale being refunded ----
  protected readonly sale = signal<PosRefundableSale | null>(null);
  /** Quantity to refund per sale line id; a line absent or at zero is not refunded. */
  protected readonly quantities = signal<Record<string, number>>({});
  protected readonly reason = signal('');
  protected readonly preview = signal<PosRefundPreview | null>(null);
  protected readonly previewing = signal(false);
  protected readonly payoutModeId = signal<string | null>(null);
  /** Phase 84 -- the parts of a split payout, added one at a time; empty means "all of it in the chosen mode". */
  protected readonly payouts = signal<PayoutRow[]>([]);
  protected readonly payoutAmountText = signal('');
  private nextPayoutKey = 1;
  private readonly payoutAmountBox = viewChild<ElementRef<HTMLInputElement>>('payoutAmountBox');
  protected readonly submitting = signal(false);
  protected readonly done = signal<CreatePosRefundResult | null>(null);
  protected readonly printing = signal<PosRefundReceipt | null>(null);
  private previewRequest = 0;

  protected readonly chosenLines = computed(() =>
    Object.entries(this.quantities())
      .filter(([, quantity]) => quantity > 0)
      .map(([invoiceLineId, quantity]) => ({ invoiceLineId, quantity })));

  protected readonly payoutModes = computed(() => this.till()?.paymentModes ?? []);

  protected readonly requiredPaisa = computed(() => toPaisa(this.preview()?.requiredPayout ?? 0));
  protected readonly addedPaisa = computed(() => this.payouts().reduce((sum, x) => sum + x.paisa, 0));
  protected readonly leftPaisa = computed(() => this.requiredPaisa() - this.addedPaisa());

  protected readonly canComplete = computed(() => {
    // Every signal read up front: a computed records only what it read (phase 47's && trap).
    const preview = this.preview();
    const previewing = this.previewing();
    const submitting = this.submitting();
    const chosen = this.chosenLines().length;
    const paidOut = this.payouts().length === 0 ? !!this.payoutModeId() : this.leftPaisa() === 0;
    return !!preview && !previewing && !submitting && chosen > 0 && (preview.requiredPayout === 0 || paidOut);
  });

  constructor() {
    this.load(this.route.snapshot.paramMap.get('locationId')!);
  }

  private load(locationId: string): void {
    this.posService.getTill(this.organizationId, locationId).subscribe({
      next: (till) => {
        this.till.set(till);
        this.payoutModeId.set(till.paymentModes.find((x) => x.kind === 'Cash')?.id ?? till.paymentModes[0]?.id ?? null);
        this.posService.getMySession(this.organizationId, locationId).subscribe({
          next: (session) => {
            this.session.set(session);
            this.loading.set(false);
            if (!session || !till.canRefund) return;

            const invoiceId = this.route.snapshot.queryParamMap.get('invoiceId');
            if (invoiceId) {
              this.selectSale(invoiceId);
            } else {
              this.search();
              afterNextRender(() => this.searchBox()?.nativeElement.focus(), { injector: this.injector });
            }
          },
          error: (err: unknown) => this.fail(err, 'Could not read your session at this till.'),
        });
      },
      error: (err: unknown) => this.fail(err, 'Could not open this till.'),
    });
  }

  // ---- Finding the sale ----

  protected search(): void {
    const till = this.till();
    if (!till) return;

    this.posService.findSales(this.organizationId, till.locationId, this.searchTerm().trim()).subscribe({
      next: (matches) => {
        this.matches.set(matches);
        this.searched.set(true);
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not search the sales.'),
    });
  }

  protected selectSale(invoiceId: string): void {
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.done.set(null);
    const till = this.till();
    if (!till) return;
    this.posService.getRefundableSale(this.organizationId, till.locationId, invoiceId).subscribe({
      next: (sale) => {
        this.sale.set(sale);
        this.quantities.set({});
        this.reason.set('');
        this.setPreview(null);
        afterNextRender(() => document.getElementById('pos-refund-sale-heading')?.focus(), { injector: this.injector });
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not read that sale.'),
    });
  }

  protected backToSearch(): void {
    this.sale.set(null);
    this.setPreview(null);
    this.done.set(null);
    this.search();
    afterNextRender(() => this.searchBox()?.nativeElement.focus(), { injector: this.injector });
  }

  // ---- Choosing what came back ----

  protected quantityOf(line: PosRefundableLine): number {
    return this.quantities()[line.invoiceLineId] ?? 0;
  }

  protected onQuantity(line: PosRefundableLine, event: Event): void {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    const quantity = Number.isFinite(value) ? Math.min(Math.max(value, 0), line.remaining) : 0;
    this.quantities.set({ ...this.quantities(), [line.invoiceLineId]: quantity });
    this.refreshPreview();
  }

  protected refundAll(): void {
    const sale = this.sale();
    if (!sale) return;
    this.quantities.set(Object.fromEntries(sale.lines.filter((x) => x.remaining > 0).map((x) => [x.invoiceLineId, x.remaining])));
    this.refreshPreview();
  }

  /** Asks the server for the refund's figure; a reply to an older request is dropped. */
  private refreshPreview(): void {
    const sale = this.sale();
    const session = this.session();
    const lines = this.chosenLines();
    if (!sale || !session) return;

    if (lines.length === 0) {
      this.setPreview(null);
      return;
    }

    const request = ++this.previewRequest;
    this.previewing.set(true);
    this.posService.previewRefund(this.organizationId, { sessionId: session.id, invoiceId: sale.invoiceId, lines }).subscribe({
      next: (preview) => {
        if (request !== this.previewRequest) return;
        this.previewing.set(false);
        this.setPreview(preview);
      },
      error: (err: unknown) => {
        if (request !== this.previewRequest) return;
        this.previewing.set(false);
        this.setPreview(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not work out the refund.');
      },
    });
  }

  protected onReason(event: Event): void {
    this.reason.set((event.target as HTMLTextAreaElement).value);
  }

  protected onPayoutMode(modeId: string): void {
    this.payoutModeId.set(modeId);
  }

  protected onPayoutAmount(event: Event): void {
    this.payoutAmountText.set((event.target as HTMLInputElement).value);
  }

  /** A new figure starts the payout afresh: parts added against the old one no longer add up to anything. */
  private setPreview(preview: PosRefundPreview | null): void {
    this.preview.set(preview);
    this.payouts.set([]);
    this.payoutAmountText.set(preview && preview.requiredPayout > 0 ? preview.requiredPayout.toFixed(2) : '');
  }

  /** Phase 84 -- one part of a split payout, in the chosen mode, at most what is still to hand back. */
  protected addPayout(): void {
    const mode = this.payoutModes().find((x) => x.id === this.payoutModeId());
    const amount = Number(this.payoutAmountText().trim());
    if (!mode) {
      this.fieldError.fail('pos-refund-payout-amount', 'Choose how this part is handed back.');
      return;
    }
    if (!Number.isFinite(amount) || amount <= 0) {
      this.fieldError.fail('pos-refund-payout-amount', `Enter the amount handed back in ${mode.name}.`);
      return;
    }
    if (Number(amount.toFixed(2)) !== amount) {
      this.fieldError.fail('pos-refund-payout-amount', 'Enter rupees and paisa, at most two decimal places.');
      return;
    }
    if (toPaisa(amount) > this.leftPaisa()) {
      this.fieldError.fail(
        'pos-refund-payout-amount',
        `Only ${money.transform(fromPaisa(this.leftPaisa()))} is still to hand back.`);
      return;
    }

    this.errorMessage.set(null);
    this.payouts.set([...this.payouts(), { key: this.nextPayoutKey++, modeId: mode.id, modeName: mode.name, paisa: toPaisa(amount) }]);
    this.payoutAmountText.set(this.leftPaisa() > 0 ? fromPaisa(this.leftPaisa()).toFixed(2) : '');
    this.payoutAmountBox()?.nativeElement.focus();
  }

  /** Its own button goes with it, so focus goes back to the amount field that adds parts. */
  protected removePayout(key: number): void {
    this.payouts.set(this.payouts().filter((x) => x.key !== key));
    this.payoutAmountText.set(fromPaisa(this.leftPaisa()).toFixed(2));
    this.payoutAmountBox()?.nativeElement.focus();
  }

  protected fromPaisa(paisa: number): number {
    return fromPaisa(paisa);
  }

  // ---- Completing ----

  protected complete(): void {
    const sale = this.sale();
    const session = this.session();
    const till = this.till();
    const preview = this.preview();
    if (!sale || !session || !till || !preview) return;

    if (!this.reason().trim()) {
      this.fieldError.fail('pos-refund-reason', 'Say why the goods came back.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const payoutModeId = this.payoutModeId();
    const parts = this.payouts();
    const payouts =
      preview.requiredPayout === 0
        ? []
        : parts.length > 0
          ? parts.map((x) => ({ paymentModeId: x.modeId, amount: fromPaisa(x.paisa) }))
          : payoutModeId
            ? [{ paymentModeId: payoutModeId, amount: preview.requiredPayout }]
            : [];
    const handedBack =
      parts.length > 0
        ? parts.map((x) => `${money.transform(fromPaisa(x.paisa))} in ${x.modeName}`).join(' and ')
        : `${money.transform(preview.requiredPayout)} in ${till.paymentModes.find((x) => x.id === payoutModeId)?.name ?? 'cash'}`;

    this.posService
      .createRefund(this.organizationId, {
        sessionId: session.id,
        locationId: till.locationId,
        invoiceId: sale.invoiceId,
        lines: this.chosenLines(),
        payouts,
        reason: this.reason().trim(),
      })
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.done.set(result);
          this.successMessage.set(
            `Refund ${result.code} complete: ${money.transform(result.grandTotal)}`
            + (result.paidOut > 0 ? `, handed back ${handedBack}` : '')
            + (result.toAccount > 0 ? `, ${money.transform(result.toAccount)} taken off ${sale.customerName}'s account` : '')
            + '.');
          if (till.printCreditNote) {
            this.print(result.id);
          }
          // Re-read the sale, so what is left to refund is the server's figure again.
          this.posService.getRefundableSale(this.organizationId, till.locationId, sale.invoiceId).subscribe({
            next: (fresh) => {
              this.sale.set(fresh);
              this.quantities.set({});
              this.setPreview(null);
              this.reason.set('');
            },
          });
          afterNextRender(() => document.getElementById('pos-refund-done')?.focus(), { injector: this.injector });
        },
        error: (err: unknown) => {
          this.submitting.set(false);
          const message = extractErrorMessage(err) ?? 'The refund was not made.';
          if (/reason|why the goods/i.test(message)) {
            this.fieldError.fail('pos-refund-reason', message);
          } else {
            this.errorMessage.set(message);
          }
        },
      });
  }

  /** Every call is a printing the server counts; above 1 the note is marked a copy. */
  protected print(creditNoteId: string): void {
    this.posService.printRefundReceipt(this.organizationId, creditNoteId).subscribe({
      next: (receipt) => {
        this.printing.set(receipt);
        afterNextRender(() => window.print(), { injector: this.injector });
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print the credit note.'),
    });
  }

  protected textOf(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  private fail(err: unknown, fallback: string): void {
    this.loading.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }
}

/** Phase 84 -- one part of a split payout. */
interface PayoutRow {
  key: number;
  modeId: string;
  modeName: string;
  paisa: number;
}
