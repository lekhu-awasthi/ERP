import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { BillOfMaterialsListItem } from '../../../core/manufacturing/manufacturing.models';
import { ManufacturingService } from '../../../core/manufacturing/manufacturing.service';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { ListChrome } from '../../../shared/pagination/list-chrome';
import { ListFilter } from '../../../shared/pagination/list-query-options';

/** Master-data list, mirroring the reference product's own BOM list columns: product, finished
 * output quantity with its unit, and a count of raw materials and by-products. */
@Component({
  selector: 'app-bom-list-page',
  imports: [RouterLink, PaginationControl, ListChrome],
  templateUrl: './bom-list-page.html',
})
export class BomListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly manufacturingService = inject(ManufacturingService);

  /**
   * Phase 34b -- the screen's search term. No date range: this list is master data, which
   * the shell's global range deliberately does not scope (see `IDateRangeFilteredQuery`).
   */
  protected readonly filter = new ListFilter();

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<BillOfMaterialsListItem[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  constructor() {
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
    // Phase 34b -- this page's own `search` parameter (phase 25) and the sweep's shared one are
    // the same server field, so the bespoke one is gone and the term now arrives through
    // ListFilter like every other list's.
    this.manufacturingService
      .listBillsOfMaterials(this.organizationId, undefined, undefined, this.page(), this.pageSize(), this.filter.options())
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load bills of materials.');
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

}
