import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PaymentsService } from '../../../core/payments/payments.service';
import { Payment, PaymentStatus } from '../../../core/payments/payments.models';

type StatusFilter = PaymentStatus | 'All';

/** Mirror of payment-list-page, filtered to Direction=Paid (Supplier Payment) -- same underlying
 * Payment aggregate/endpoint, see payments.models.ts's PaymentDirection doc comment. */
@Component({
  selector: 'app-supplier-payment-list-page',
  imports: [RouterLink],
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
    this.paymentsService.listPayments(this.organizationId, status === 'All' ? undefined : status).subscribe({
      next: (items) => {
        this.items.set(items.filter((p) => p.direction === 'Paid'));
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load supplier payments.');
      },
    });
  }
}
