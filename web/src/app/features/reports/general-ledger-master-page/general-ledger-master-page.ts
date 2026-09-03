import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import {
  GeneralLedgerMasterRowDto,
  GlSourceDocumentType,
} from '../../../core/accounting/accounting.models';
import { PagedResult, DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { triggerBlobDownload } from '../../../shared/download-file';
import { GL_SOURCE_DOCUMENT_TYPES, glDetailRoute, txnTypeLabel } from '../gl-report-shared';

const EMPTY_REPORT: PagedResult<GeneralLedgerMasterRowDto> = {
  items: [],
  page: 1,
  pageSize: DEFAULT_PAGE_SIZE,
  totalCount: 0,
};

/**
 * Phase 26a -- GL Master Report (Reports &gt; Accounting), Admin-only
 * (Reports.GeneralLedgerMaster.View). The denormalised fact table over the general ledger: one row
 * per posted line, carrying its document and its account's full classification -- the Sales Master
 * Report shape applied to the GL.
 *
 * <p>The live report's SubAccount column is not shown: this codebase has no subledger accounts at
 * all, and the column was empty on every row of the live report anyway. See
 * GeneralLedgerMasterQuery.</p>
 */
@Component({
  selector: 'app-general-ledger-master-page',
  imports: [RouterLink, PaginationControl, AmountPipe, NepaliDatePipe, BsDateInput],
  templateUrl: './general-ledger-master-page.html',
})
export class GeneralLedgerMasterPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly documentTypes = GL_SOURCE_DOCUMENT_TYPES;
  protected readonly txnTypeLabel = txnTypeLabel;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<PagedResult<GeneralLedgerMasterRowDto>>(EMPTY_REPORT);

  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());
  protected readonly documentType = signal<GlSourceDocumentType | ''>('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.load();
  }

  protected detailRoute(row: GeneralLedgerMasterRowDto): string[] | null {
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

  protected onDocumentTypeChange(event: Event): void {
    this.documentType.set((event.target as HTMLSelectElement).value as GlSourceDocumentType | '');
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
      .exportGeneralLedgerMaster(
        this.organizationId, this.fromDate(), this.toDate(), this.documentType() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `GeneralLedgerMaster_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the GL Master Report.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService
      .getGeneralLedgerMaster(
        this.organizationId, this.fromDate(), this.toDate(), this.documentType() || null,
        this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the GL Master Report.');
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
