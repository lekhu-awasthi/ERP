import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { RatioAnalysisDto } from '../../../core/accounting/accounting.models';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';

/**
 * Read-only report screen -- Phase 19's RatioAnalysisQuery, grouped by the 4 confirmed categories
 * (decision #6, no live check needed -- erp-module-scan.md already fully specifies the ratio list).
 */
@Component({
  selector: 'app-ratio-analysis-page',
  imports: [RouterLink, AmountPipe, BsDateInput],
  templateUrl: './ratio-analysis-page.html',
})
export class RatioAnalysisPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<RatioAnalysisDto | null>(null);

  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());

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

  protected export(): void {
    this.exporting.set(true);
    this.accountingService.exportRatioAnalysis(this.organizationId, this.fromDate(), this.toDate()).subscribe({
      next: (blob) => {
        this.exporting.set(false);
        triggerBlobDownload(blob, `RatioAnalysis_${this.fromDate()}_${this.toDate()}.xlsx`);
      },
      error: (err: unknown) => {
        this.exporting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Ratio Analysis.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService.getRatioAnalysis(this.organizationId, this.fromDate(), this.toDate()).subscribe({
      next: (report) => {
        this.report.set(report);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Ratio Analysis.');
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
