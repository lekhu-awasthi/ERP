import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { IncomeStatementDto } from '../../../core/accounting/accounting.models';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { triggerBlobDownload } from '../../../shared/download-file';

/** Read-only report screen -- roadmap Phase 8a's IncomeStatementQuery, Income minus Expense
 * accounts with activity in [fromDate, toDate].
 *
 * Phase 26a adds FR-9.1's Compare switch and the .xlsx export this screen never had. The
 * comparison window is the same-length period immediately preceding, chosen server-side and
 * echoed back so the column header names the real dates; note that with Compare on the row set
 * widens to the union of accounts with movement in either window (see IncomeStatementQuery). */
@Component({
  selector: 'app-income-statement-page',
  imports: [RouterLink, AmountPipe, NepaliDatePipe, BsDateInput],
  templateUrl: './income-statement-page.html',
})
export class IncomeStatementPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<IncomeStatementDto | null>(null);
  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());
  protected readonly compare = signal(false);
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

  protected onCompareChange(event: Event): void {
    this.compare.set((event.target as HTMLInputElement).checked);
    this.load();
  }

  protected exportReport(): void {
    this.exporting.set(true);
    this.accountingService
      .exportIncomeStatement(this.organizationId, this.fromDate(), this.toDate(), this.compare())
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `IncomeStatement_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Income Statement.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService
      .getIncomeStatement(this.organizationId, this.fromDate(), this.toDate(), this.compare())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Income Statement.');
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
