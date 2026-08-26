import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesService } from '../../../core/sales/sales.service';
import { Quotation, QuotationStatus } from '../../../core/sales/sales.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { CustomStatus } from '../../../core/configuration/configuration.models';
import { CustomStatusPicker } from '../../../shared/custom-status/custom-status-picker';

type StatusFilter = QuotationStatus | 'All';

/** List-page chrome for Quotation, same pattern as journal-voucher-list-page. */
@Component({
  selector: 'app-quotation-list-page',
  imports: [RouterLink, PaginationControl, CustomStatusPicker],
  templateUrl: './quotation-list-page.html',
})
export class QuotationListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly salesService = inject(SalesService);
  private readonly configurationService = inject(ConfigurationService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<Quotation[]>([]);
  protected readonly statusFilter = signal<StatusFilter>('All');
  protected readonly customStatusOptions = signal<CustomStatus[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly statuses: StatusFilter[] = ['All', 'Draft', 'Approved'];

  constructor() {
    this.load();
    this.configurationService.listCustomStatuses(this.organizationId).subscribe({
      next: (all) => this.customStatusOptions.set(all.filter((s) => s.isActive && s.documentType === 'Quotation')),
    });
  }

  protected onCustomStatusChange(itemId: string, customStatusId: string | null): void {
    this.items.update((items) => items.map((item) => (item.id === itemId ? { ...item, customStatusId } : item)));
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
    this.salesService
      .listQuotations(this.organizationId, status === 'All' ? undefined : status, this.page(), this.pageSize())
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load quotations.');
        },
      });
  }
}
