import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesService } from '../../../core/sales/sales.service';
import { Invoice, InvoiceStatus } from '../../../core/sales/sales.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ListChrome, documentSortOptions } from '../../../shared/pagination/list-chrome';
import { ListFilter } from '../../../shared/pagination/list-query-options';
import { DateRangeService } from '../../../shared/platform/date-range.service';
import { LocationName } from '../../../shared/locations/location-name';
import { StatusBanner } from '../../../shared/a11y/status-banner';

type StatusFilter = InvoiceStatus | 'All';

@Component({
  selector: 'app-invoice-list-page',
  imports: [RouterLink, PaginationControl, NepaliDatePipe, ListChrome, LocationName, StatusBanner],
  templateUrl: './invoice-list-page.html',
})
export class InvoiceListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly salesService = inject(SalesService);

  /** Phase 34b -- the screen's search term plus the shell's global date range. */
  protected readonly filter = new ListFilter(inject(DateRangeService), () => this.reloadForDateRange());

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<Invoice[]>([]);
  protected readonly statusFilter = signal<StatusFilter>('All');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly statuses: StatusFilter[] = ['All', 'Draft', 'Approved'];

  /**
   * Phase 40 — the first consumer of the chrome's `Sort by` control, which 34b built and left empty.
   *
   * <b>Two options, because two is how many orderings this table is indexed for.</b> 34b's re-entry
   * condition was "the first list whose default ordering someone complains about", which nobody can
   * check; 34c supplies one that can. `TenantIndexConvention` gives every document
   * `(OrganizationId, CreatedAt DESC)` for its list screen and `(OrganizationId, Date)` for its date
   * range, and those two indexes are exactly these two entries. A third option — by number, by
   * customer — would be a sort over the whole filtered set on a table 34c measured at 50 000 rows,
   * and the pager would hide it, because page 1 still comes back with ten rows on it.
   *
   * The invoice list is the right first consumer for the same reason: it is the one 34c measured.
   */
  protected readonly sortOptions = documentSortOptions('Invoice date');

  constructor() {
    this.load();
  }

  /**
   * Phase 32, extracted in phase 35a -- the chrome's Billing Location filter, now the same control
   * and the same `ListFilter.location` slot the other fourteen document lists use.
   */
  protected onLocation(locationId: string): void {
    this.filter.location.set(locationId);
    this.page.set(1);
    this.load();
  }

  protected selectStatus(status: StatusFilter): void {
    this.statusFilter.set(status);
    this.page.set(1);
    this.load();
  }

  /** Resets to page 1 like every other filter on this page — see `onSearch`. */
  protected onSort(sort: string): void {
    this.filter.sort.set(sort);
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
      .listInvoices(
        this.organizationId,
        status === 'All' ? undefined : status,
        this.page(),
        this.pageSize(),
        this.filter.options(),
      )
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load invoices.');
        },
      });
  }
  /**
   * Phase 34b -- the shared list chrome's search box. Resets to page 1, because staying on page 4
   * of a result set the filter has just shrunk to one page shows an empty list and reads as
   * "search found nothing".
   */
  protected onSearch(term: string): void {
    this.filter.search.set(term);
    this.page.set(1);
    this.load();
  }

  /**
   * Phase 34b -- the shell's global date range changed under an open list. Reload from page 1: the
   * window that produced the current page no longer applies, and the chrome is already showing the
   * new one.
   */
  private reloadForDateRange(): void {
    this.page.set(1);
    this.load();
  }

}
