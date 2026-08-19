import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { JournalVoucher, JournalVoucherStatus } from '../../../core/accounting/accounting.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';

type StatusFilter = JournalVoucherStatus | 'All';

/** List-page chrome for JournalVoucher -- rows navigate to journal-voucher-detail-page, the same
 * list->detail split Contacts/Products established (see phase-3-status.md). This is the first
 * list page to also show a Status badge, since JournalVoucher is the first Draft->Approve
 * document type. */
@Component({
  selector: 'app-journal-voucher-list-page',
  imports: [RouterLink, PaginationControl],
  templateUrl: './journal-voucher-list-page.html',
})
export class JournalVoucherListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<JournalVoucher[]>([]);
  protected readonly statusFilter = signal<StatusFilter>('All');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly statuses: StatusFilter[] = ['All', 'Draft', 'Approved'];

  constructor() {
    this.load();
  }

  protected selectStatus(status: StatusFilter): void {
    this.statusFilter.set(status);
    this.page.set(1);
    this.load();
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.page.set(1);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    const status = this.statusFilter();
    this.accountingService
      .listJournalVouchers(this.organizationId, status === 'All' ? undefined : status, this.page(), this.pageSize())
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load journal vouchers.');
        },
      });
  }
}
