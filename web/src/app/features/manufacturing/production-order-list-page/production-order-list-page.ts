import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ProductionOrderListItem, ProductionOrderStatus } from '../../../core/manufacturing/manufacturing.models';
import { ManufacturingService } from '../../../core/manufacturing/manufacturing.service';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { CustomStatusPicker } from '../../../shared/custom-status/custom-status-picker';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { CustomStatus } from '../../../core/configuration/configuration.models';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

type StatusFilter = ProductionOrderStatus | 'All';

/** List-page chrome for Production Order, the same shape every ApprovableTransaction list uses.
 * Status tabs mirror the reference product's own Approved/Draft tabs. */
@Component({
  selector: 'app-production-order-list-page',
  imports: [RouterLink, PaginationControl, CustomStatusPicker, NepaliDatePipe],
  templateUrl: './production-order-list-page.html',
})
export class ProductionOrderListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly manufacturingService = inject(ManufacturingService);

  private readonly configurationService = inject(ConfigurationService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<ProductionOrderListItem[]>([]);
  protected readonly statusFilter = signal<StatusFilter>('All');

  /** Phase 27a: the tenant's pipeline for this document type, loaded once. Filtered to
   * active definitions of this type only -- an inactive status is refused server-side. */
  protected readonly customStatusOptions = signal<CustomStatus[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly statuses: StatusFilter[] = ['All', 'Draft', 'Approved', 'Converted'];

  constructor() {
    this.load();
    this.configurationService.listCustomStatuses(this.organizationId).subscribe({
      next: (all) => this.customStatusOptions.set(all.filter((c) => c.isActive && c.documentType === 'ProductionOrder')),
    });
  }

  /** The picker saves itself; this only keeps the in-memory row in step so the control
   * does not snap back to its old value before the next reload. */
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
    this.manufacturingService
      .listProductionOrders(this.organizationId, status === 'All' ? undefined : status, this.page(), this.pageSize())
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load production orders.');
        },
      });
  }
}
