import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { AccountingService } from '../../../core/accounting/accounting.service';
import {
  BankAccountDto,
  BankStatementLineDto,
  BookTransactionDto,
} from '../../../core/accounting/accounting.models';
import { extractErrorMessage } from '../../../core/auth/api-error';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 56 — the two-pane reconciliation matcher.
 *
 * <p>Imported bank statement lines on the left, this tenant's own movements through the same
 * account on the right. Tick some of each and press Reconcile. The reference product's
 * `/accounting/bank-accounts/:id/manual-reconcile`, read live on 2026-09-21.</p>
 *
 * <p><b>The rule, and it is the server's.</b> The two selected sums must be equal, with at least one
 * row ticked on each side. This screen disables the button when they are not — but the button is a
 * courtesy, not the enforcement: the endpoint refuses an unbalanced pair with a 400 naming both
 * totals, exactly as the reference product's own does. So the difference is shown rather than
 * merely blocking, because a user whose selection is out by 40 needs to know by how much.</p>
 *
 * <p><b>Both panes are unreconciled-only.</b> A row leaves its pane the moment it is matched, which
 * is what makes the screen finishable: the work is done when both sides are empty.</p>
 */
@Component({
  selector: 'app-bank-reconcile-page',
  imports: [RouterLink, StatusBanner, AmountPipe, NepaliDatePipe],
  templateUrl: './bank-reconcile-page.html',
})
export class BankReconcilePage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly bankAccountId = this.route.snapshot.paramMap.get('accountId')!;

  protected readonly loadingBank = signal(true);
  protected readonly loadingBook = signal(true);
  protected readonly reconciling = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly statusMessage = signal<string | null>(null);

  protected readonly account = signal<BankAccountDto | null>(null);
  protected readonly bankRows = signal<BankStatementLineDto[]>([]);
  protected readonly bookRows = signal<BookTransactionDto[]>([]);

  /**
   * The ticked rows, held as ids rather than as flags on the rows themselves: both panes are
   * replaced wholesale on every reload, and a flag would silently vanish (the same reasoning the
   * statement page's selection carries).
   */
  protected readonly selectedBankIds = signal<readonly string[]>([]);
  protected readonly selectedBookIds = signal<readonly string[]>([]);

  /**
   * Both totals are signed from the account's point of view — money in is positive — which is the
   * one convention this whole screen is expressed in. On the book side that means `debit - credit`,
   * already computed server-side into `signedAmount` so the sign rule lives in exactly one place
   * (`BankMovementTotal`), not in a template.
   */
  protected readonly bankTotal = computed(() =>
    this.bankRows()
      .filter((row) => this.selectedBankIds().includes(row.id))
      .reduce((sum, row) => sum + row.signedAmount, 0),
  );

  protected readonly bookTotal = computed(() =>
    this.bookRows()
      .filter((row) => this.selectedBookIds().includes(row.id))
      .reduce((sum, row) => sum + row.signedAmount, 0),
  );

  protected readonly difference = computed(() => this.bankTotal() - this.bookTotal());

  /**
   * The reference product's own gate, transcribed from its bundle: the sums must agree *and* each
   * side must actually hold something. Zero is not the disqualifier — an empty side is — which is
   * why this asks about the counts separately. Two lines netting to nothing is a real match.
   */
  protected readonly canReconcile = computed(
    () =>
      this.selectedBankIds().length > 0 &&
      this.selectedBookIds().length > 0 &&
      this.difference() === 0,
  );

  constructor() {
    this.loadAccount();
    this.loadBank();
    this.loadBook();
  }

  protected isBankSelected(id: string): boolean {
    return this.selectedBankIds().includes(id);
  }

  protected isBookSelected(id: string): boolean {
    return this.selectedBookIds().includes(id);
  }

  protected toggleBank(id: string, checked: boolean): void {
    this.selectedBankIds.update((ids) => (checked ? [...ids, id] : ids.filter((x) => x !== id)));
  }

  protected toggleBook(id: string, checked: boolean): void {
    this.selectedBookIds.update((ids) => (checked ? [...ids, id] : ids.filter((x) => x !== id)));
  }

  protected reconcile(): void {
    if (!this.canReconcile()) {
      return;
    }

    const bankCount = this.selectedBankIds().length;
    const bookCount = this.selectedBookIds().length;

    this.reconciling.set(true);
    this.errorMessage.set(null);
    this.statusMessage.set(null);

    this.accountingService
      .createBankReconciliation(
        this.organizationId,
        this.bankAccountId,
        this.selectedBankIds(),
        this.selectedBookIds(),
      )
      .subscribe({
        next: () => {
          this.reconciling.set(false);
          this.statusMessage.set(
            `Reconciled ${bankCount === 1 ? '1 statement line' : `${bankCount} statement lines`} ` +
              `against ${bookCount === 1 ? '1 transaction' : `${bookCount} transactions`}.`,
          );
          this.selectedBankIds.set([]);
          this.selectedBookIds.set([]);
          this.loadBank();
          this.loadBook();
        },
        error: (err: unknown) => {
          this.reconciling.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not reconcile those transactions.');
        },
      });
  }

  private loadAccount(): void {
    this.accountingService.listBankAccounts(this.organizationId, true, 1, 200).subscribe({
      next: (result) => this.account.set(result.items.find((a) => a.id === this.bankAccountId) ?? null),
    });
  }

  private loadBank(): void {
    this.loadingBank.set(true);

    this.accountingService
      .listBankStatementLines(this.organizationId, this.bankAccountId, 1, 100, { reconciled: false })
      .subscribe({
        next: (result) => {
          this.bankRows.set(result.items);
          this.loadingBank.set(false);
        },
        error: (err: unknown) => {
          this.loadingBank.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the bank statement.');
        },
      });
  }

  private loadBook(): void {
    this.loadingBook.set(true);

    this.accountingService
      .listBookTransactions(this.organizationId, this.bankAccountId, 1, 100, { reconciled: false })
      .subscribe({
        next: (result) => {
          this.bookRows.set(result.items);
          this.loadingBook.set(false);
        },
        error: (err: unknown) => {
          this.loadingBook.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load this account’s transactions.');
        },
      });
  }
}
