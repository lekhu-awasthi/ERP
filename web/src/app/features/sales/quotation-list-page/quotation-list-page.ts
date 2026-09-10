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
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ListChrome } from '../../../shared/pagination/list-chrome';
import { ListFilter } from '../../../shared/pagination/list-query-options';
import { DateRangeService } from '../../../shared/platform/date-range.service';

type StatusFilter = QuotationStatus | 'All';

/** List-page chrome for Quotation, same pattern as journal-voucher-list-page. */
@Component({
  selector: 'app-quotation-list-page',
  imports: [RouterLink, PaginationControl, CustomStatusPicker, NepaliDatePipe, ListChrome],
  templateUrl: './quotation-list-page.html',
})
export class QuotationListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly salesService = inject(SalesService);
  private readonly configurationService = inject(ConfigurationService);

  /** Phase 34b -- the screen's search term plus the shell's global date range. */
  protected readonly filter = new ListFilter(inject(DateRangeService), () => this.reloadForDateRange());

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
      .listQuotations(this.organizationId, status === 'All' ? undefined : status, this.page(), this.pageSize(), this.filter.options())
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
