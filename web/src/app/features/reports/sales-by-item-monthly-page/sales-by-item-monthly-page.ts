import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import {
  MONTHS_PER_QUARTER,
  QUARTER_LABELS,
  TradeByItemMonthlyDto,
} from '../../../core/trade/trade-reports.models';
import { TradeReportsService } from '../../../core/trade/trade-reports.service';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { currentFiscalYear, fiscalYearLabel, supportedFiscalYears } from '../../../shared/formatting/bs-fiscal-year';

/**
 * Sales By Item (Monthly) -- confirmed live 2026-09-03.
 *
 * **Keyed by a Bikram Sambat fiscal year, not a date range.** Twelve BS month columns in fiscal
 * order (Shrawan first, Asar of the following BS year last), a quarter subtotal after every third,
 * and a row Total. The measure is Net Sales, not Total Amount -- proved against the live figures.
 *
 * The column set comes from the server, which owns the BS calendar (`BsCalendar`); this component
 * only lays it out. Header and body cells are built by the same interleaving helper so they cannot
 * drift apart.
 */
@Component({
  selector: 'app-sales-by-item-monthly-page',
  imports: [PaginationControl, AmountPipe],
  templateUrl: './sales-by-item-monthly-page.html',
})
export class SalesByItemMonthlyPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(TradeReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly fiscalYears = supportedFiscalYears();
  protected readonly fiscalYearLabel = fiscalYearLabel;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<TradeByItemMonthlyDto | null>(null);

  protected readonly fiscalYear = signal(currentFiscalYear());

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  /** Month labels with a quarter label after every third, matching the live header. */
  protected readonly headerCells = computed(() => {
    const columns = this.report()?.columns ?? [];
    const cells: { label: string; isQuarter: boolean }[] = [];
    columns.forEach((column, index) => {
      cells.push({ label: column.label, isQuarter: false });
      if ((index + 1) % MONTHS_PER_QUARTER === 0) {
        cells.push({ label: QUARTER_LABELS[(index + 1) / MONTHS_PER_QUARTER - 1], isQuarter: true });
      }
    });
    return cells;
  });

  constructor() {
    this.load();
  }

  /** The same interleaving as the header, applied to one row's figures. */
  protected cells(monthly: number[], quarters: number[]): { value: number; isQuarter: boolean }[] {
    const cells: { value: number; isQuarter: boolean }[] = [];
    monthly.forEach((value, index) => {
      cells.push({ value, isQuarter: false });
      if ((index + 1) % MONTHS_PER_QUARTER === 0) {
        cells.push({ value: quarters[(index + 1) / MONTHS_PER_QUARTER - 1], isQuarter: true });
      }
    });
    return cells;
  }

  protected onFiscalYearChange(event: Event): void {
    this.fiscalYear.set(Number((event.target as HTMLSelectElement).value));
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
      .exportTradeByItemMonthly(this.organizationId, 'sales-by-item-monthly', this.fiscalYear(), full, page, pageSize)
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `SalesByItemMonthly_BS${this.fiscalYear()}-${this.fiscalYear() + 1}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Sales By Item (Monthly).');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getTradeByItemMonthly(this.organizationId, 'sales-by-item-monthly', this.fiscalYear(), this.page(), this.pageSize())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Sales By Item (Monthly).');
        },
      });
  }
}
