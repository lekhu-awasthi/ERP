import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesSummaryMode, SalesSummaryReportDto } from '../../../core/trade/trade-reports.models';
import { TradeReportsService } from '../../../core/trade/trade-reports.service';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { currentFiscalYear, fiscalYearLabel, supportedFiscalYears } from '../../../shared/formatting/bs-fiscal-year';

/**
 * Sales Summary Report -- confirmed live 2026-09-03.
 *
 * Keyed by a Bikram Sambat fiscal year with a **Select Mode** picker (Date or Month), and, unlike
 * every other report in this phase, **no footer total row** -- the live report has none, and a sum
 * over "one row per month" and "one row per day" would mean different things in the two modes.
 *
 * Only periods with activity appear, which is the live behaviour and the opposite of the Monthly
 * crosstabs' fixed twelve columns: a crosstab's columns are an axis, a summary's rows are its data.
 *
 * **The live Service Charge column is omitted.** It is driven by a product-level
 * `service_charge_applicable` flag this codebase does not model, and it printed "-" on every row of
 * both modes even on the reference tenant. A column of hard zeroes would look like an answer; see
 * docs/phase-26b-status.md.
 */
@Component({
  selector: 'app-sales-summary-report-page',
  imports: [PaginationControl, AmountPipe, NepaliDatePipe],
  templateUrl: './sales-summary-report-page.html',
})
export class SalesSummaryReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(TradeReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly fiscalYears = supportedFiscalYears();
  protected readonly fiscalYearLabel = fiscalYearLabel;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<SalesSummaryReportDto | null>(null);

  protected readonly fiscalYear = signal(currentFiscalYear());
  protected readonly mode = signal<SalesSummaryMode>('Month');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.load();
  }

  protected onFiscalYearChange(event: Event): void {
    this.fiscalYear.set(Number((event.target as HTMLSelectElement).value));
    this.reload();
  }

  protected onModeChange(event: Event): void {
    this.mode.set((event.target as HTMLSelectElement).value as SalesSummaryMode);
    this.reload();
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

  private reload(): void {
    this.page.set(1);
    this.load();
  }

  private runExport(full: boolean, page: number, pageSize: number): void {
    this.exporting.set(true);
    this.reports
      .exportSalesSummaryReport(this.organizationId, this.fiscalYear(), this.mode(), full, page, pageSize)
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `SalesSummaryReport_BS${this.fiscalYear()}-${this.fiscalYear() + 1}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Sales Summary Report.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getSalesSummaryReport(this.organizationId, this.fiscalYear(), this.mode(), this.page(), this.pageSize())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Sales Summary Report.');
        },
      });
  }
}
