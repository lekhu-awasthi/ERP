import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { BalanceSheetDto } from '../../../core/accounting/accounting.models';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';

/** Read-only report screen -- roadmap Phase 8a's BalanceSheetQuery, Asset/Liability/Equity
 * accounts grouped by top-level AccountGroup (full-subtree rollup) as of a cutoff date, with a
 * synthetic "Net Income (Current Period)" plug line under Equity (see accounting.models.ts'
 * AccountGroupBalanceDto -- the plug row's groupId is the empty guid). */
@Component({
  selector: 'app-balance-sheet-page',
  imports: [RouterLink, AmountPipe, BsDateInput],
  templateUrl: './balance-sheet-page.html',
})
export class BalanceSheetPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<BalanceSheetDto | null>(null);
  protected readonly asOfDate = signal(this.today());

  constructor() {
    this.load();
  }

  protected onAsOfDateChange(value: string): void {
    this.asOfDate.set(value);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService.getBalanceSheet(this.organizationId, this.asOfDate()).subscribe({
      next: (report) => {
        this.report.set(report);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Balance Sheet.');
      },
    });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
