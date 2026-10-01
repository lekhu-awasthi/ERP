import { Component, Injector, afterNextRender, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { Account } from '../../../core/accounting/accounting.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { AuthService } from '../../../core/auth/auth.service';
import { extractErrorMessage, extractWarningKind } from '../../../core/auth/api-error';
import { fromPaisa, toPaisa } from '../../../core/pos/pos-bill';
import { forgetStoredCart, storedHoldCount } from '../../../core/pos/pos-cart';
import {
  DenominationCount,
  PosCashMovementDirection,
  PosReceipt,
  PosRefundReceipt,
  PosSession,
  PosSessionRefund,
  PosSessionSale,
  PosTill,
} from '../../../core/pos/pos.models';
import { PosService } from '../../../core/pos/pos.service';
import { FieldError, FieldErrorMessage } from '../../../shared/a11y/field-error';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { DenominationCountInput } from '../denomination-count/denomination-count';
import { PosReceiptView } from '../pos-receipt/pos-receipt';
import { PosRefundReceiptView } from '../pos-receipt/pos-refund-receipt';

/** The app's one money format (lakh/crore grouping), for messages built in code. */
const money = new AmountPipe();

/**
 * Phase 62 -- one session: its X report while open and its Z report once closed, both read from
 * `GET /pos/sessions/{id}` and so from phase 61's one reader (`PosSalesReader`); the drawer's Cash
 * In/Out and its close with a count; and the session's sales, each reprintable.
 *
 * <p><b>Who sees what.</b> The server decides: your own session, or anyone's with
 * `Pos.Session.ViewAll`. Only the owner moves cash or closes (phase 61), so the forms show for the
 * owner of an open session and nobody else.</p>
 *
 * <p>Also where a `PosSession` GL row drills down to (phase 61 section 5 left it nowhere to go).</p>
 */
@Component({
  selector: 'app-pos-session-page',
  imports: [
    RouterLink, StatusBanner, FieldErrorMessage, AmountPipe, NepaliDatePipe, DenominationCountInput, PosReceiptView,
    PosRefundReceiptView,
  ],
  templateUrl: './pos-session-page.html',
})
export class PosSessionPage {
  private readonly route = inject(ActivatedRoute);
  private readonly posService = inject(PosService);
  private readonly accountingService = inject(AccountingService);
  private readonly auth = inject(AuthService);
  private readonly injector = inject(Injector);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  /** Phase 47's shape: a refusal that names a field marks it, describes it and moves focus there. */
  protected readonly fieldError = new FieldError(this.errorMessage);

  protected readonly session = signal<PosSession | null>(null);
  protected readonly sales = signal<PosSessionSale[]>([]);
  /** Phase 63 -- the refunds paid out of this drawer. */
  protected readonly refunds = signal<PosSessionRefund[]>([]);
  protected readonly till = signal<PosTill | null>(null);
  protected readonly accounts = signal<Account[]>([]);

  /** Moving cash and closing are the owner's (phase 61 Decision F); reading may be wider. */
  protected readonly isOwnOpen = computed(() => {
    const s = this.session();
    return !!s && s.status === 'Open' && s.userId === this.auth.currentUser()?.userId;
  });

  // ---- Cash In / Out ----
  protected readonly direction = signal<PosCashMovementDirection>('Out');
  protected readonly movementAmount = signal<number | null>(null);
  protected readonly movementAccountId = signal('');
  protected readonly movementNote = signal('');
  protected readonly cashWarning = signal<string | null>(null);

  /** Any account but the drawer's own, which a movement in or out of the drawer cannot name. */
  protected readonly movementAccounts = computed(() =>
    this.accounts()
      .filter((x) => x.isActive && x.id !== this.session()?.cashAccountId)
      .sort((a, b) => a.code.localeCompare(b.code)));

  // ---- Close ----
  protected readonly countedAmount = signal<number | null>(null);
  protected readonly closingCount = signal<DenominationCount[]>([]);
  protected readonly closingNote = signal('');
  protected readonly heldCarts = signal(0);
  protected readonly submitting = signal(false);

  /** Null until something is counted, so the form never announces a shortfall nobody has counted. */
  protected readonly counted = computed(() => {
    if (!this.till()?.cashVerificationRequired) {
      return this.countedAmount();
    }

    const rows = this.closingCount();
    return rows.length === 0 ? null : rows.reduce((sum, x) => sum + x.value * x.count, 0);
  });

  /** Counted less expected, in rupees: negative is short. Null until something is counted. */
  protected readonly difference = computed(() => {
    const counted = this.counted();
    const s = this.session();
    return counted === null || !s ? null : fromPaisa(toPaisa(counted) - toPaisa(s.expectedCash));
  });

  // ---- Reprint ----
  protected readonly printing = signal<PosReceipt | null>(null);
  protected readonly printingRefund = signal<PosRefundReceipt | null>(null);

  constructor() {
    this.load();
  }

  private load(): void {
    const sessionId = this.route.snapshot.paramMap.get('sessionId')!;

    this.posService.getSession(this.organizationId, sessionId).subscribe({
      next: (session) => {
        this.session.set(session);
        this.loading.set(false);
        this.loadSales(session.id);
        this.loadRefunds(session.id);
        this.heldCarts.set(storedHoldCount(this.organizationId, session.locationId, session.id));

        if (this.isOwnOpen()) {
          this.posService.getTill(this.organizationId, session.locationId).subscribe({
            next: (till) => this.till.set(till),
            error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not read the till.'),
          });
          this.accountingService.listAllAccounts(this.organizationId).subscribe({
            next: (accounts) => this.accounts.set(accounts),
            error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the accounts.'),
          });
        }
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the session.');
      },
    });
  }

  private loadSales(sessionId: string): void {
    this.posService.listSessionSales(this.organizationId, sessionId).subscribe({
      next: (sales) => this.sales.set(sales),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the sales.'),
    });
  }

  private loadRefunds(sessionId: string): void {
    this.posService.listSessionRefunds(this.organizationId, sessionId).subscribe({
      next: (refunds) => this.refunds.set(refunds),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the refunds.'),
    });
  }

  protected numberOf(event: Event): number | null {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    return Number.isFinite(value) ? value : null;
  }

  protected textOf(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  protected onAccountChange(event: Event): void {
    this.movementAccountId.set((event.target as HTMLSelectElement).value);
  }

  protected recordMovement(override = false): void {
    const s = this.session();
    const amount = this.movementAmount();
    if (!s) return;
    if (amount === null || amount <= 0) {
      this.fieldError.fail('pos-movement-amount', 'Enter the amount of cash moved.');
      return;
    }
    if (!this.movementAccountId()) {
      this.fieldError.fail('pos-movement-account', 'Choose where the cash came from or went to.');
      return;
    }

    this.begin();
    this.cashWarning.set(null);

    this.posService
      .recordCashMovement(this.organizationId, s.id, {
        direction: this.direction(),
        amount,
        accountId: this.movementAccountId(),
        note: this.movementNote().trim() || null,
        overrideNegativeCashBalanceWarning: override,
      })
      .subscribe({
        next: (session) => {
          this.submitting.set(false);
          this.session.set(session);
          this.successMessage.set(
            `Cash ${this.direction() === 'In' ? 'in' : 'out'} of ${money.transform(amount)} recorded. `
            + `The drawer should now hold ${money.transform(session.expectedCash)}.`);
          this.movementAmount.set(null);
          this.movementNote.set('');
        },
        error: (err: unknown) => {
          this.submitting.set(false);
          if (extractWarningKind(err) === 'NegativeCashBalance') {
            this.cashWarning.set(extractErrorMessage(err) ?? 'This would take the account below zero.');
            return;
          }
          this.errorMessage.set(extractErrorMessage(err) ?? 'The cash movement was not recorded.');
        },
      });
  }

  protected closeSession(): void {
    const s = this.session();
    const till = this.till();
    if (!s || !till) return;

    const counted = this.counted();
    if (counted === null) {
      if (till.cashVerificationRequired) {
        this.errorMessage.set('Count the drawer before closing it.');
      } else {
        this.fieldError.fail('pos-close-amount', 'Count the drawer before closing it.');
      }
      return;
    }
    if (this.difference() !== 0 && !this.closingNote().trim()) {
      this.fieldError.fail('pos-close-note', 'The count differs from what the drawer should hold. Say why in the note.');
      return;
    }

    this.begin();
    this.posService
      .closeSession(this.organizationId, s.id, {
        countedAmount: till.cashVerificationRequired ? null : counted,
        denominations: till.cashVerificationRequired ? this.closingCount() : null,
        note: this.closingNote().trim() || null,
      })
      .subscribe({
        next: (session) => {
          this.submitting.set(false);
          this.session.set(session);
          forgetStoredCart(this.organizationId, session.locationId, session.id);
          this.heldCarts.set(0);
          this.successMessage.set(`Session ${session.code} is closed.`);
        },
        error: (err: unknown) => {
          this.submitting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'The session was not closed.');
        },
      });
  }

  /** Every reprint is a printing the server counts and marks as a copy (Decision B). */
  protected reprint(sale: PosSessionSale): void {
    this.errorMessage.set(null);
    this.posService.printReceipt(this.organizationId, sale.invoiceId).subscribe({
      next: (receipt) => {
        this.printingRefund.set(null);
        this.printing.set(receipt);
        this.sales.set(this.sales().map((x) => (x.invoiceId === sale.invoiceId ? { ...x, printCount: receipt.printNumber } : x)));
        this.successMessage.set(
          receipt.printNumber > 1
            ? `${receipt.code} printed as copy ${receipt.printNumber - 1} (printed ${receipt.printNumber} times).`
            : `${receipt.code} printed.`);
        afterNextRender(() => window.print(), { injector: this.injector });
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print the receipt.'),
    });
  }

  /** Phase 63 -- a credit note's reprint, counted and marked like a sale's. */
  protected reprintRefund(refund: PosSessionRefund): void {
    this.errorMessage.set(null);
    this.posService.printRefundReceipt(this.organizationId, refund.creditNoteId).subscribe({
      next: (receipt) => {
        this.printing.set(null);
        this.printingRefund.set(receipt);
        this.refunds.set(this.refunds().map((x) =>
          (x.creditNoteId === refund.creditNoteId ? { ...x, printCount: receipt.printNumber } : x)));
        this.successMessage.set(
          receipt.printNumber > 1
            ? `${receipt.code} printed as copy ${receipt.printNumber - 1} (printed ${receipt.printNumber} times).`
            : `${receipt.code} printed.`);
        afterNextRender(() => window.print(), { injector: this.injector });
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print the credit note.'),
    });
  }

  private begin(): void {
    this.submitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);
  }
}
