import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { TrialBalanceDto } from '../../../core/accounting/accounting.models';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { triggerBlobDownload } from '../../../shared/download-file';

/** Read-only report screen -- roadmap Phase 8a's TrialBalanceQuery, every active Account's net
 * Debit/Credit balance as of a cutoff date.
 *
 * Phase 26a adds FR-9.1's Compare switch and the .xlsx export this screen never had. Compare is a
 * plain signal driving one reload, not a second request the template merges: the server returns
 * both windows on one response with the compared date echoed back, so the extra column headers can
 * name the real date (see ComparePeriod). The switch is tracked in its own signal written by the
 * change handler, not read off a FormControl -- the app is zoneless and a computed() over a plain
 * control value caches forever (phase-17). */
@Component({
  selector: 'app-trial-balance-page',
  imports: [RouterLink, AmountPipe, NepaliDatePipe, BsDateInput],
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
  protected readonly compare = signal(false);
  protected readonly exporting = signal(false);

  constructor() {
    this.load();
  }

  protected onAsOfDateChange(value: string): void {
    this.asOfDate.set(value);
    this.load();
  }

  protected onCompareChange(event: Event): void {
    this.compare.set((event.target as HTMLInputElement).checked);
    this.load();
  }

  protected exportReport(): void {
    this.exporting.set(true);
    this.accountingService.exportTrialBalance(this.organizationId, this.asOfDate(), this.compare()).subscribe({
      next: (blob) => {
        this.exporting.set(false);
        triggerBlobDownload(blob, `TrialBalance_${this.asOfDate()}.xlsx`);
      },
      error: (err: unknown) => {
        this.exporting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Trial Balance.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService.getTrialBalance(this.organizationId, this.asOfDate(), this.compare()).subscribe({
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
