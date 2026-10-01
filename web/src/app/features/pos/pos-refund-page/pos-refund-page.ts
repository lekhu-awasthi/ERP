import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
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
 * <p><b>One payout mode per refund on this screen.</b> The API takes several; a refund split across
 * modes is rare enough at a counter that the screen offers the one choice, Cash first.</p>
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
  protected readonly submitting = signal(false);
  protected readonly done = signal<CreatePosRefundResult | null>(null);
  protected readonly printing = signal<PosRefundReceipt | null>(null);
  private previewRequest = 0;

  protected readonly chosenLines = computed(() =>
    Object.entries(this.quantities())
      .filter(([, quantity]) => quantity > 0)
      .map(([invoiceLineId, quantity]) => ({ invoiceLineId, quantity })));

  protected readonly payoutModes = computed(() => this.till()?.paymentModes ?? []);

  protected readonly canComplete = computed(() => {
    const preview = this.preview();
    return (
      !!preview &&
      !this.previewing() &&
      !this.submitting() &&
      this.chosenLines().length > 0 &&
      (preview.requiredPayout === 0 || !!this.payoutModeId())
    );
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
    this.posService.getRefundableSale(this.organizationId, invoiceId).subscribe({
      next: (sale) => {
        this.sale.set(sale);
        this.quantities.set({});
        this.reason.set('');
        this.preview.set(null);
        afterNextRender(() => document.getElementById('pos-refund-sale-heading')?.focus(), { injector: this.injector });
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not read that sale.'),
    });
  }

  protected backToSearch(): void {
    this.sale.set(null);
    this.preview.set(null);
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
      this.preview.set(null);
      return;
    }

    const request = ++this.previewRequest;
    this.previewing.set(true);
    this.posService.previewRefund(this.organizationId, { sessionId: session.id, invoiceId: sale.invoiceId, lines }).subscribe({
      next: (preview) => {
        if (request !== this.previewRequest) return;
        this.previewing.set(false);
        this.preview.set(preview);
      },
      error: (err: unknown) => {
        if (request !== this.previewRequest) return;
        this.previewing.set(false);
        this.preview.set(null);
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
    const payouts = preview.requiredPayout > 0 && payoutModeId ? [{ paymentModeId: payoutModeId, amount: preview.requiredPayout }] : [];

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
          const mode = till.paymentModes.find((x) => x.id === payoutModeId);
          this.successMessage.set(
            `Refund ${result.code} complete: ${money.transform(result.grandTotal)}`
            + (result.paidOut > 0 ? `, ${money.transform(result.paidOut)} handed back in ${mode?.name ?? 'cash'}` : '')
            + (result.toAccount > 0 ? `, ${money.transform(result.toAccount)} taken off ${sale.customerName}'s account` : '')
            + '.');
          if (till.printCreditNote) {
            this.print(result.id);
          }
          // Re-read the sale, so what is left to refund is the server's figure again.
          this.posService.getRefundableSale(this.organizationId, sale.invoiceId).subscribe({
            next: (fresh) => {
              this.sale.set(fresh);
              this.quantities.set({});
              this.preview.set(null);
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
