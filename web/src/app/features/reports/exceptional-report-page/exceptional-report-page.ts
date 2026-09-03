import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { ExceptionalReportRowDto } from '../../../core/reports/catalogue-reports.models';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';

/**
 * Phase 26c -- twelve fixed anomaly rows, each a magnitude with a DR/CR marker except the two
 * inventory rows, which carry none: a stock valuation does not sit on a side of the ledger.
 */
@Component({
  selector: 'app-exceptional-report-page',
  imports: [RouterLink, AmountPipe, BsDateInput],
  templateUrl: './exceptional-report-page.html',
})
export class ExceptionalReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(CatalogueReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<ExceptionalReportRowDto[]>([]);

  protected readonly fromDate = signal(firstOfMonth());
  protected readonly toDate = signal(today());

  protected readonly exporting = signal(false);

  constructor() {
    this.load();
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.load();
  }

  protected onToDateChange(value: string): void {
    this.toDate.set(value);
    this.load();
  }

  protected exportReport(): void {
    this.exporting.set(true);
    this.reports.exportExceptionalReport(this.organizationId, this.fromDate(), this.toDate()).subscribe({
      next: (blob) => {
        this.exporting.set(false);
        triggerBlobDownload(blob, `ExceptionalReport_${this.fromDate()}_${this.toDate()}.xlsx`);
      },
      error: (err: unknown) => {
        this.exporting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Exceptional Report.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports.getExceptionalReport(this.organizationId, this.fromDate(), this.toDate()).subscribe({
      next: (report) => {
        this.rows.set(report.rows);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Exceptional Report.');
      },
    });
  }
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function firstOfMonth(): string {
  const now = new Date();
  return new Date(Date.UTC(now.getFullYear(), now.getMonth(), 1)).toISOString().slice(0, 10);
}
