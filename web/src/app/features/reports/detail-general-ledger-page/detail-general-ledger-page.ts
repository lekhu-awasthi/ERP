import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import {
  Account,
  DetailGeneralLedgerAccountDto,
  DetailGeneralLedgerRowDto,
} from '../../../core/accounting/accounting.models';
import { PagedResult, DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { triggerBlobDownload } from '../../../shared/download-file';
import { glDetailRoute, txnTypeLabel } from '../gl-report-shared';

const EMPTY_REPORT: PagedResult<DetailGeneralLedgerAccountDto> = {
  items: [],
  page: 1,
  pageSize: DEFAULT_PAGE_SIZE,
  totalCount: 0,
};

/**
 * Phase 26a -- Detail General Ledger (Reports &gt; Accounting), Admin-only
 * (Reports.DetailGeneralLedger.View). One section per account: an Opening Balance row, every
 * posting in date order with a running balance, and a Closing Balance row whose Debit and Credit
 * cells hold the section's period totals.
 *
 * <p><b>The pager counts accounts, not rows.</b> A running balance is only correct if its section
 * is whole, so splitting one account's postings across two pages would print a closing figure that
 * does not match the rows above it. See DetailGeneralLedgerQuery.</p>
 */
@Component({
  selector: 'app-detail-general-ledger-page',
  imports: [RouterLink, PaginationControl, AmountPipe, NepaliDatePipe, BsDateInput],
  templateUrl: './detail-general-ledger-page.html',
})
export class DetailGeneralLedgerPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly txnTypeLabel = txnTypeLabel;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<PagedResult<DetailGeneralLedgerAccountDto>>(EMPTY_REPORT);
  protected readonly accounts = signal<Account[]>([]);

  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());
  protected readonly accountId = signal<string>('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.accountingService.listAllAccounts(this.organizationId).subscribe({
      next: (accounts) => this.accounts.set(accounts),
      error: () => {
        // A picker degrading to an empty dropdown is not fatal to the report itself.
      },
    });
    this.load();
  }

  protected detailRoute(row: DetailGeneralLedgerRowDto): string[] | null {
    return glDetailRoute(this.organizationId, row.documentType, row.documentId, row.direction);
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.page.set(1);
    this.load();
  }

  protected onToDateChange(value: string): void {
    this.toDate.set(value);
    this.page.set(1);
    this.load();
  }

  protected onAccountChange(event: Event): void {
    this.accountId.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
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

  protected exportCurrentView(): void {
    this.runExport(false, this.page(), this.pageSize());
  }

  protected exportFullDataset(): void {
    this.runExport(true, 1, this.pageSize());
  }

  private runExport(full: boolean, page: number, pageSize: number): void {
    this.exporting.set(true);
    this.accountingService
      .exportDetailGeneralLedger(
        this.organizationId, this.fromDate(), this.toDate(), this.accountId() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `DetailGeneralLedger_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Detail General Ledger.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService
      .getDetailGeneralLedger(
        this.organizationId, this.fromDate(), this.toDate(), this.accountId() || null,
        this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Detail General Ledger.');
        },
      });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private firstOfMonth(): string {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
  }
}
