import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { CashTransfer, CashTransferStatus } from '../../../core/accounting/accounting.models';

type StatusFilter = CashTransferStatus | 'All';

/** Same list-page chrome as journal-voucher-list-page. */
@Component({
  selector: 'app-cash-transfer-list-page',
  imports: [RouterLink],
  templateUrl: './cash-transfer-list-page.html',
})
export class CashTransferListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<CashTransfer[]>([]);
  protected readonly statusFilter = signal<StatusFilter>('All');

  protected readonly statuses: StatusFilter[] = ['All', 'Draft', 'Approved'];

  constructor() {
    this.load();
  }

  protected selectStatus(status: StatusFilter): void {
    this.statusFilter.set(status);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    const status = this.statusFilter();
    this.accountingService.listCashTransfers(this.organizationId, status === 'All' ? undefined : status).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load cash transfers.');
      },
    });
  }
}
