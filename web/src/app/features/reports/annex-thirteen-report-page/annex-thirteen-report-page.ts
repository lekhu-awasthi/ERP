import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { AnnexThirteenReportDto } from '../../../core/purchasing/purchasing.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';

/**
 * Read-only report screen -- roadmap Phase 8e's AnnexThirteenReportQuery, a per-Contact rollup of
 * Sales and Purchase activity (six buckets: Service/Goods Purchase Capital/Others, Service/Goods
 * Sales) filtered to Contacts whose total period activity meets a threshold (100,000 NPR default,
 * editable here). Date-range plus a Threshold Amount input -- no Contact/Product filters, same
 * filing-period-register shape decision as VAT Summary/TDS Report. No totals footer -- each row is
 * already a per-Contact total, a footer summing across Contacts isn't a meaningful Annex 13 number
 * (see phase-8e-status.md's scope decision). Paginated (Phase 16c).
 */
@Component({
  selector: 'app-annex-thirteen-report-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput],
  templateUrl: './annex-thirteen-report-page.html',
})
export class AnnexThirteenReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly purchasingService = inject(PurchasingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<AnnexThirteenReportDto | null>(null);
  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());
  protected readonly thresholdAmount = signal(100000);

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

  protected onThresholdAmountChange(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.thresholdAmount.set(Number.isFinite(value) && value >= 0 ? value : 0);
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
    this.purchasingService
      .exportAnnexThirteenReport(
        this.organizationId, this.fromDate(), this.toDate(), this.thresholdAmount(), full, page, pageSize)
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `AnnexThirteenReport_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Annex 13 Report.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.purchasingService
      .getAnnexThirteenReport(
        this.organizationId, this.fromDate(), this.toDate(), this.thresholdAmount(), this.page(), this.pageSize())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Annex 13 Report.');
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
