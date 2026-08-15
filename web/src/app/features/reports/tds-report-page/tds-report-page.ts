import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { TdsReportDto } from '../../../core/purchasing/purchasing.models';

/**
 * Read-only report screen -- roadmap Phase 8d's TdsReportQuery, a deductee-wise TDS register
 * listing one row per Approved PurchaseBill/Expense/DebitNote that withheld TDS. Date-range only,
 * no Contact/Product filters -- a filing-period register, same shape decision as the VAT Summary
 * Report page (see phase-8d-status.md's scope decision). DebitNote rows are signed negative --
 * they reverse a source PurchaseBill's TDS rather than netting into its row, so the table shows
 * both the original deduction and the reversal as their own lines.
 */
@Component({
  selector: 'app-tds-report-page',
  imports: [RouterLink],
  templateUrl: './tds-report-page.html',
})
export class TdsReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly purchasingService = inject(PurchasingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<TdsReportDto | null>(null);
  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());

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

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.purchasingService.getTdsReport(this.organizationId, this.fromDate(), this.toDate()).subscribe({
      next: (report) => {
        this.report.set(report);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the TDS Report.');
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
