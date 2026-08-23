import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { CashFlowSummaryDto } from '../../../core/accounting/accounting.models';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Read-only report screen -- Phase 19's CashFlowSummaryQuery, a direct-method summary of actual
 * Bank/Cash account movements (decision #2, live-confirmed -- no Operating/Investing/Financing
 * classification anywhere in the reference product or this codebase's Chart of Accounts).
 */
@Component({
  selector: 'app-cash-flow-summary-page',
  imports: [RouterLink],
  templateUrl: './cash-flow-summary-page.html',
})
export class CashFlowSummaryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<CashFlowSummaryDto | null>(null);
  protected readonly bankAccounts = signal<Account[]>([]);

  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());
  protected readonly bankAccountId = signal('');

  protected readonly exporting = signal(false);

  protected readonly bankAccountOptions = computed(() => this.bankAccounts().filter((a) => a.kind === 'Bank' || a.kind === 'Cash'));

  constructor() {
    this.accountingService.listAllAccounts(this.organizationId).subscribe({ next: (accounts) => this.bankAccounts.set(accounts) });
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

  protected onBankAccountChange(event: Event): void {
    this.bankAccountId.set((event.target as HTMLSelectElement).value);
    this.load();
  }

  protected export(): void {
    this.exporting.set(true);
    this.accountingService
      .exportCashFlowSummary(this.organizationId, this.fromDate(), this.toDate(), this.bankAccountId() || null)
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `CashFlowSummary_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Cash Flow Summary.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService
      .getCashFlowSummary(this.organizationId, this.fromDate(), this.toDate(), this.bankAccountId() || null)
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Cash Flow Summary.');
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
