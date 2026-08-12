import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesService } from '../../../core/sales/sales.service';
import { Quotation, QuotationStatus } from '../../../core/sales/sales.models';

type StatusFilter = QuotationStatus | 'All';

/** List-page chrome for Quotation, same pattern as journal-voucher-list-page. */
@Component({
  selector: 'app-quotation-list-page',
  imports: [RouterLink],
  templateUrl: './quotation-list-page.html',
})
export class QuotationListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly salesService = inject(SalesService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<Quotation[]>([]);
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
    this.salesService.listQuotations(this.organizationId, status === 'All' ? undefined : status).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load quotations.');
      },
    });
  }
}
