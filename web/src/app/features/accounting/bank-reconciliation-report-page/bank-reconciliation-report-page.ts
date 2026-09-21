import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { BankReconciliationReportDto } from '../../../core/accounting/accounting.models';
import { extractErrorMessage } from '../../../core/auth/api-error';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 56 — the Reconciliation Report, read live from the reference product on 2026-09-21:
 *
 * <pre>
 *   Balance In TIGG App        As of 21-09-2026   NPR 791
 *   Balance in Cash In Hand    As of 21-09-2026   NPR 1,582
 *   Difference                                    NPR -791
 *   > Unrecognized Transaction in Tigg App        NPR 0
 *   > Unrecognized Transaction in Bank            NPR 791
 * </pre>
 *
 * <p><b>One as-of date, not a range</b> — its filter bar carries a single Date control, which is the
 * right shape for the subject: a reconciliation report answers "do the two records agree today",
 * and a balance is cumulative rather than a period figure. The date goes through
 * `app-bs-date-input` like every date a user types in this app (phase 23, enforced by
 * `sweep-guard.spec.ts`).</p>
 *
 * <p><b>The two balances come from different date fields, deliberately.</b> The bank side cuts off
 * on the statement line's value date and the book side on the posting date, because they are two
 * records kept by two different people — which is the whole premise of reconciling them. Each row
 * says which date it is using.</p>
 */
@Component({
  selector: 'app-bank-reconciliation-report-page',
  imports: [RouterLink, StatusBanner, BsDateInput, AmountPipe, NepaliDatePipe],
  templateUrl: './bank-reconciliation-report-page.html',
})
export class BankReconciliationReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly bankAccountId = this.route.snapshot.paramMap.get('accountId')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<BankReconciliationReportDto | null>(null);

  /** Null lets the server answer "as of today", in Nepal's wall clock rather than the browser's. */
  protected readonly asOfDate = signal<string | null>(null);

  /** The two sections start collapsed, exactly as the reference product's do. */
  protected readonly bookExpanded = signal(false);
  protected readonly bankExpanded = signal(false);

  constructor() {
    this.load();
  }

  protected onAsOfDateChange(value: string | null): void {
    this.asOfDate.set(value);
    this.load();
  }

  protected toggleBook(): void {
    this.bookExpanded.update((x) => !x);
  }

  protected toggleBank(): void {
    this.bankExpanded.update((x) => !x);
  }

  private load(): void {
    this.loading.set(true);

    this.accountingService
      .getBankReconciliationReport(this.organizationId, this.bankAccountId, this.asOfDate())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the reconciliation report.');
        },
      });
  }
}
