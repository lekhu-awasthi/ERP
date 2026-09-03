import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { GlSourceDocumentType, JournalReportEntryDto } from '../../../core/accounting/accounting.models';
import { PagedResult, DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { triggerBlobDownload } from '../../../shared/download-file';
import { GL_SOURCE_DOCUMENT_TYPES, txnTypeLabel } from '../gl-report-shared';

const EMPTY_REPORT: PagedResult<JournalReportEntryDto> = {
  items: [],
  page: 1,
  pageSize: DEFAULT_PAGE_SIZE,
  totalCount: 0,
};

/**
 * Phase 26a -- the Journal report (Reports &gt; Accounting), Admin-only
 * (Reports.JournalReport.View). One block per posted document: its own GL lines, then a Total row
 * whose two figures are equal by construction.
 *
 * <p>Paged at document granularity, which is what the live report does and the only paging that
 * keeps a block's Total row correct -- see JournalReportQuery.</p>
 */
@Component({
  selector: 'app-journal-report-page',
  imports: [PaginationControl, AmountPipe, NepaliDatePipe, BsDateInput],
  templateUrl: './journal-report-page.html',
})
export class JournalReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly documentTypes = GL_SOURCE_DOCUMENT_TYPES;
  protected readonly txnTypeLabel = txnTypeLabel;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<PagedResult<JournalReportEntryDto>>(EMPTY_REPORT);

  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());
  protected readonly documentType = signal<GlSourceDocumentType | ''>('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
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
      .exportJournalReport(
        this.organizationId, this.fromDate(), this.toDate(), this.documentType() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `JournalReport_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Journal report.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService
      .getJournalReport(
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
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Journal report.');
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
