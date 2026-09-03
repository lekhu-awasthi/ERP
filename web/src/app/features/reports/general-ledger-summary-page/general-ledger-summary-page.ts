import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import {
  Account,
  AccountGroup,
  GeneralLedgerSummaryRowDto,
} from '../../../core/accounting/accounting.models';
import { PagedResult, DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { triggerBlobDownload } from '../../../shared/download-file';

const EMPTY_REPORT: PagedResult<GeneralLedgerSummaryRowDto> = {
  items: [],
  page: 1,
  pageSize: DEFAULT_PAGE_SIZE,
  totalCount: 0,
};

/**
 * Phase 26a -- General Ledger Summary (Reports &gt; Accounting), the one report in this phase
 * granted to Member as well as Admin: it is a bounded per-account rollup with no transaction
 * detail, the same shape as Trial Balance.
 *
 * <p>It is the Trial Balance with a period -- opening, movement and closing per account, the
 * four-figure shape the live Trial Balance has and ours does not. Balances render as a magnitude
 * plus their own DR/CR marker, which is what the server sends, so this template never has to know
 * which side is normal for which account.</p>
 */
@Component({
  selector: 'app-general-ledger-summary-page',
  imports: [PaginationControl, AmountPipe, BsDateInput],
  templateUrl: './general-ledger-summary-page.html',
})
export class GeneralLedgerSummaryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<PagedResult<GeneralLedgerSummaryRowDto>>(EMPTY_REPORT);
  protected readonly groups = signal<AccountGroup[]>([]);
  protected readonly accounts = signal<Account[]>([]);

  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());
  protected readonly groupId = signal<string>('');
  protected readonly accountId = signal<string>('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.accountingService.listAccountGroups(this.organizationId).subscribe({
      next: (groups) => this.groups.set(groups),
      error: () => {
        // A picker degrading to an empty dropdown is not fatal to the report itself.
      },
    });
    this.accountingService.listAllAccounts(this.organizationId).subscribe({
      next: (accounts) => this.accounts.set(accounts),
      error: () => {
        // Same.
      },
    });
    this.load();
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

  protected onGroupChange(event: Event): void {
    this.groupId.set((event.target as HTMLSelectElement).value);
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
      .exportGeneralLedgerSummary(
        this.organizationId, this.fromDate(), this.toDate(), this.groupId() || null, this.accountId() || null,
        full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `GeneralLedgerSummary_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the General Ledger Summary.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService
      .getGeneralLedgerSummary(
        this.organizationId, this.fromDate(), this.toDate(), this.groupId() || null, this.accountId() || null,
        this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the General Ledger Summary.');
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
