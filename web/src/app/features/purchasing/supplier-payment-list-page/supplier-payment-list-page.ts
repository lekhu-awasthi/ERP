import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PaymentsService } from '../../../core/payments/payments.service';
import { Payment, PaymentStatus } from '../../../core/payments/payments.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';

type StatusFilter = PaymentStatus | 'All';

/** Mirror of payment-list-page, filtered to Direction=Paid (Supplier Payment) -- same underlying
 * Payment aggregate/endpoint, see payments.models.ts's PaymentDirection doc comment. */
@Component({
  selector: 'app-supplier-payment-list-page',
  imports: [RouterLink, PaginationControl],
  templateUrl: './supplier-payment-list-page.html',
})
export class SupplierPaymentListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly paymentsService = inject(PaymentsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<Payment[]>([]);
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
    this.paymentsService
      .listPayments(this.organizationId, status === 'All' ? undefined : status, 'Paid', this.page(), this.pageSize())
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load supplier payments.');
        },
      });
  }
}
