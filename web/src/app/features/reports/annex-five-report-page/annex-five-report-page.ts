import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesService } from '../../../core/sales/sales.service';
import { AnnexFiveReportDto } from '../../../core/sales/sales.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Read-only report screen -- roadmap Phase 8f's AnnexFiveReportQuery, a flat Sales bill register
 * (one row per Approved Invoice/CreditNote). Mirrors Tigg's confirmed live "Annex 5 Materialised
 * View Report" screen only where this codebase has the underlying data -- Fiscal_Year (BS
 * calendar), Sync with IRD/Is_Realtime/Transaction_Id (no CBMS integration), Is_Bill_Printed/
 * Printed_Time/Printed_By (no print tracking), Payment_Method, VAT_Refund_Amount, Discount, and
 * Entered_By are all omitted -- see phase-8f-status.md's scope decision. Date-range only, no
 * Contact/Product filters -- same filing-period-register shape as VAT Summary/TDS Report.
 * Paginated (Phase 16c).
 */
@Component({
  selector: 'app-annex-five-report-page',
  imports: [RouterLink, PaginationControl],
  templateUrl: './annex-five-report-page.html',
})
export class AnnexFiveReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly salesService = inject(SalesService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<AnnexFiveReportDto | null>(null);
  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.load();
  }

  protected onFromDateChange(event: Event): void {
    this.fromDate.set((event.target as HTMLInputElement).value);
    this.page.set(1);
    this.load();
  }

  protected onToDateChange(event: Event): void {
    this.toDate.set((event.target as HTMLInputElement).value);
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
    this.salesService.exportAnnexFiveReport(this.organizationId, this.fromDate(), this.toDate(), full, page, pageSize).subscribe({
      next: (blob) => {
        this.exporting.set(false);
        triggerBlobDownload(blob, `AnnexFiveReport_${this.fromDate()}_${this.toDate()}.xlsx`);
      },
      error: (err: unknown) => {
        this.exporting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Annex 5 Report.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.salesService
      .getAnnexFiveReport(this.organizationId, this.fromDate(), this.toDate(), this.page(), this.pageSize())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Annex 5 Report.');
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
