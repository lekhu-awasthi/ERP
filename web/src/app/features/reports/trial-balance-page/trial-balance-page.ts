import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { TrialBalanceDto } from '../../../core/accounting/accounting.models';

/** Read-only report screen -- roadmap Phase 8a's TrialBalanceQuery, every active Account's net
 * Debit/Credit balance as of a cutoff date. */
@Component({
  selector: 'app-trial-balance-page',
  imports: [RouterLink],
  templateUrl: './trial-balance-page.html',
})
export class TrialBalancePage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<TrialBalanceDto | null>(null);
  protected readonly asOfDate = signal(this.today());

  constructor() {
    this.load();
  }

  protected onAsOfDateChange(event: Event): void {
    this.asOfDate.set((event.target as HTMLInputElement).value);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService.getTrialBalance(this.organizationId, this.asOfDate()).subscribe({
      next: (report) => {
        this.report.set(report);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Trial Balance.');
      },
    });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
