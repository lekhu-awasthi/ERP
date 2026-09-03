import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { BalanceSheetDto } from '../../../core/accounting/accounting.models';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { triggerBlobDownload } from '../../../shared/download-file';

/** Read-only report screen -- roadmap Phase 8a's BalanceSheetQuery, Asset/Liability/Equity
 * accounts grouped by top-level AccountGroup (full-subtree rollup) as of a cutoff date, with a
 * synthetic "Net Income (Current Period)" plug line under Equity (see accounting.models.ts'
 * AccountGroupBalanceDto -- the plug row's groupId is the empty guid).
 *
 * Phase 26a adds FR-9.1's Compare switch (prior-year same date, chosen server-side and echoed
 * back so the column header names the real date) and the .xlsx export this screen never had. */
@Component({
  selector: 'app-balance-sheet-page',
  imports: [RouterLink, AmountPipe, NepaliDatePipe, BsDateInput],
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
    this.accountingService.exportBalanceSheet(this.organizationId, this.asOfDate(), this.compare()).subscribe({
      next: (blob) => {
        this.exporting.set(false);
        triggerBlobDownload(blob, `BalanceSheet_${this.asOfDate()}.xlsx`);
      },
      error: (err: unknown) => {
        this.exporting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Balance Sheet.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService.getBalanceSheet(this.organizationId, this.asOfDate(), this.compare()).subscribe({
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
