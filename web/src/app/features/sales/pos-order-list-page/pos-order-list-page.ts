import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { POS_TAB_LABELS } from '../../../core/pos/pos.models';
import { POS_ORDER_TYPES, PosOrder, PosOrderStatus, PosOrderType } from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ReportLocationFilter } from '../../../shared/locations/report-location-filter';
import { ListChrome, documentSortOptions } from '../../../shared/pagination/list-chrome';
import { ListFilter } from '../../../shared/pagination/list-query-options';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { DateRangeService } from '../../../shared/platform/date-range.service';

type StatusFilter = PosOrderStatus | 'All';

/**
 * Phase 64 -- Sales > POS Orders: every restaurant order, open or voided, read-only. It is the surface
 * phase 49's rule says a chosen divergence owes: the vendor's open tab is a Sales Order, so its back
 * office sees it among Sales Orders; ours is a POS order, and without this list an open tab would be
 * invisible to everyone not standing at the till.
 *
 * <p>The document-list chrome (search, the shell's date range, sort), status tabs, the order type, and
 * the location filter reports use -- an order is not one of the location-scoped document types the
 * list chrome's own filter knows. A row expands in place to its lines and kitchen tickets; there is no
 * detail page, because nothing here is edited.</p>
 */
@Component({
  selector: 'app-pos-order-list-page',
  imports: [StatusBanner, AmountPipe, NepaliDatePipe, ReportLocationFilter, ListChrome, PaginationControl],
  templateUrl: './pos-order-list-page.html',
})
export class PosOrderListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly restaurantService = inject(PosRestaurantService);

  protected readonly filter = new ListFilter(inject(DateRangeService), () => this.reloadFromFirstPage());

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly typeLabels = POS_TAB_LABELS;
  protected readonly orderTypes = POS_ORDER_TYPES;
  protected readonly statuses: StatusFilter[] = ['All', 'Open', 'Voided'];
  protected readonly sortOptions = documentSortOptions('Order date');

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<PosOrder[]>([]);
  protected readonly statusFilter = signal<StatusFilter>('Open');
  protected readonly orderType = signal<PosOrderType | ''>('');
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  constructor() {
    this.load();
  }

  protected selectStatus(status: StatusFilter): void {
    this.statusFilter.set(status);
    this.reloadFromFirstPage();
  }

  protected onOrderType(event: Event): void {
    this.orderType.set((event.target as HTMLSelectElement).value as PosOrderType | '');
    this.reloadFromFirstPage();
  }

  protected onLocation(locationId: string): void {
    this.filter.location.set(locationId);
    this.reloadFromFirstPage();
  }

  protected onSearch(term: string): void {
    this.filter.search.set(term);
    this.reloadFromFirstPage();
  }

  protected onSort(sort: string): void {
    this.filter.sort.set(sort);
    this.reloadFromFirstPage();
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.reloadFromFirstPage();
  }

  protected toggle(orderId: string): void {
    const next = new Set(this.expanded());
    if (next.has(orderId)) next.delete(orderId);
    else next.add(orderId);
    this.expanded.set(next);
  }

  private reloadFromFirstPage(): void {
    this.page.set(1);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    const status = this.statusFilter();
    this.restaurantService
      .listOrders(
        this.organizationId,
        {
          status: status === 'All' ? undefined : status,
          orderType: this.orderType() || undefined,
          page: this.page(),
          pageSize: this.pageSize(),
        },
        this.filter.options(),
      )
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.errorMessage.set(null);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the POS orders.');
        },
      });
  }
}
