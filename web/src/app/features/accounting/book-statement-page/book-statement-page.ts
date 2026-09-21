import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { BankAccountDto, BookTransactionDto } from '../../../core/accounting/accounting.models';
import { extractErrorMessage } from '../../../core/auth/api-error';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { DateRangeService } from '../../../shared/platform/date-range.service';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ListChrome } from '../../../shared/pagination/list-chrome';
import { ListFilter } from '../../../shared/pagination/list-query-options';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 56 — the Book Statement: this tenant's own movements through one cash-and-bank account,
 * beside the bank's version of the same thing.
 *
 * <p>The reference product's `/accounting/bank-accounts/:id/book-statement`, which calls the same
 * `/gl-transactions` endpoint its matcher does but without the unreconciled filter — so this shows
 * everything and marks what has been matched.</p>
 *
 * <p><b>No Sort by menu, and no search box.</b> Both are decisions rather than omissions, and both
 * come from the same fact: a GL posting has exactly one date and does not carry its document's
 * number. `ListBookTransactionsQuery` states each in full, and both are recorded in the sweep
 * guards with their premises asserted independently.</p>
 */
@Component({
  selector: 'app-book-statement-page',
  imports: [RouterLink, ListChrome, PaginationControl, StatusBanner, AmountPipe, NepaliDatePipe],
  templateUrl: './book-statement-page.html',
})
export class BookStatementPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly bankAccountId = this.route.snapshot.paramMap.get('accountId')!;

  protected readonly filter = new ListFilter(inject(DateRangeService), () => this.reloadForDateRange());

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<BookTransactionDto[]>([]);
  protected readonly account = signal<BankAccountDto | null>(null);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  constructor() {
    this.loadAccount();
    this.load();
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.page.set(1);
    this.load();
  }

  private reloadForDateRange(): void {
    this.page.set(1);
    this.load();
  }

  private loadAccount(): void {
    this.accountingService.listBankAccounts(this.organizationId, true, 1, 200).subscribe({
      next: (result) => this.account.set(result.items.find((a) => a.id === this.bankAccountId) ?? null),
    });
  }

  private load(): void {
    this.loading.set(true);

    this.accountingService
      .listBookTransactions(
        this.organizationId,
        this.bankAccountId,
        this.page(),
        this.pageSize(),
        this.filter.options(),
      )
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load this account’s transactions.');
        },
      });
  }
}
