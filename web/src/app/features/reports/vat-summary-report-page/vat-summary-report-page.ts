import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { VatSummaryReportDto } from '../../../core/accounting/accounting.models';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Read-only report screen -- roadmap Phase 8c's VatSummaryReportQuery, a standard Nepal
 * VAT-return-style summary netting Invoice/CreditNote and PurchaseBill/DebitNote lines into
 * three VatRate buckets per side plus Output/Input VAT totals and Net VAT Payable/Refundable.
 * Date-range only, no Contact/Product/Warehouse filters -- this is a filing-period summary, not a
 * transaction register (see phase-8c-status.md's scope decision).
 */
@Component({
  selector: 'app-vat-summary-report-page',
  imports: [RouterLink],
  templateUrl: './vat-summary-report-page.html',
})
export class VatSummaryReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<VatSummaryReportDto | null>(null);
  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());

  protected readonly exporting = signal(false);

  constructor() {
    this.load();
  }

  protected onFromDateChange(event: Event): void {
    this.fromDate.set((event.target as HTMLInputElement).value);
    this.load();
  }

  protected onToDateChange(event: Event): void {
    this.toDate.set((event.target as HTMLInputElement).value);
    this.load();
  }

  protected exportReport(): void {
    this.exporting.set(true);
    this.accountingService.exportVatSummaryReport(this.organizationId, this.fromDate(), this.toDate()).subscribe({
      next: (blob) => {
        this.exporting.set(false);
        triggerBlobDownload(blob, `VatSummaryReport_${this.fromDate()}_${this.toDate()}.xlsx`);
      },
      error: (err: unknown) => {
        this.exporting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the VAT Summary Report.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService.getVatSummaryReport(this.organizationId, this.fromDate(), this.toDate()).subscribe({
      next: (report) => {
        this.report.set(report);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the VAT Summary Report.');
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
