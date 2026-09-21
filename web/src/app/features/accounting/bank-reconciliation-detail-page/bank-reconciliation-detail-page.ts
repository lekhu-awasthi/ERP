import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { BankReconciliationDetailDto } from '../../../core/accounting/accounting.models';
import { extractErrorMessage } from '../../../core/auth/api-error';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 56 — one reconciliation: both sides, who made it and when, and the Unreconcile button.
 *
 * <p>The reference product renders this as a drawer over the matcher; here it is a page, because
 * the row that leads to it is a Status badge on a list and a page is linkable, reloadable and
 * bookmarkable where a drawer pushed from router state is none of those (phase 53's own lesson
 * about `/accounting/recon`).</p>
 *
 * <p><b>Unreconcile takes the whole thing.</b> There is no un-match of one row from a group, and
 * there should not be: a reconciliation's one invariant is that its two sides total the same
 * figure, so removing a row from either side leaves a record that breaks the only rule it had.</p>
 */
@Component({
  selector: 'app-bank-reconciliation-detail-page',
  imports: [RouterLink, StatusBanner, AmountPipe, NepaliDatePipe],
  templateUrl: './bank-reconciliation-detail-page.html',
})
export class BankReconciliationDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly bankAccountId = this.route.snapshot.paramMap.get('accountId')!;
  protected readonly reconciliationId = this.route.snapshot.paramMap.get('reconciliationId')!;

  protected readonly loading = signal(true);
  protected readonly unreconciling = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly detail = signal<BankReconciliationDetailDto | null>(null);

  constructor() {
    this.load();
  }

  protected unreconcile(): void {
    this.unreconciling.set(true);
    this.errorMessage.set(null);

    this.accountingService
      .deleteBankReconciliation(this.organizationId, this.bankAccountId, this.reconciliationId)
      .subscribe({
        next: () => {
          this.unreconciling.set(false);
          void this.router.navigate([
            '/organizations',
            this.organizationId,
            'accounting',
            'bank-accounts',
            this.bankAccountId,
            'reconcile',
          ]);
        },
        error: (err: unknown) => {
          this.unreconciling.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not unreconcile this match.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);

    this.accountingService
      .getBankReconciliation(this.organizationId, this.bankAccountId, this.reconciliationId)
      .subscribe({
        next: (detail) => {
          this.detail.set(detail);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load this reconciliation.');
        },
      });
  }
}
