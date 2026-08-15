import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { AnnexThirteenReportDto } from '../../../core/purchasing/purchasing.models';

/**
 * Read-only report screen -- roadmap Phase 8e's AnnexThirteenReportQuery, a per-Contact rollup of
 * Sales and Purchase activity (six buckets: Service/Goods Purchase Capital/Others, Service/Goods
 * Sales) filtered to Contacts whose total period activity meets a threshold (100,000 NPR default,
 * editable here). Date-range plus a Threshold Amount input -- no Contact/Product filters, same
 * filing-period-register shape decision as VAT Summary/TDS Report. No totals footer -- each row is
 * already a per-Contact total, a footer summing across Contacts isn't a meaningful Annex 13 number
 * (see phase-8e-status.md's scope decision).
 */
@Component({
  selector: 'app-annex-thirteen-report-page',
  imports: [RouterLink],
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

  protected onThresholdAmountChange(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.thresholdAmount.set(Number.isFinite(value) && value >= 0 ? value : 0);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.purchasingService
      .getAnnexThirteenReport(this.organizationId, this.fromDate(), this.toDate(), this.thresholdAmount())
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
