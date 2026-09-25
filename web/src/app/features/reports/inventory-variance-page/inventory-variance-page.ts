import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import {
  InventoryTrackingMode,
  InventoryVarianceDirection,
  InventoryVarianceRowDto,
} from '../../../core/reports/catalogue-reports.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, ProductCategory } from '../../../core/catalog/catalog.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { nepalToday } from '../../../shared/formatting/nepal-time';
import { ReportLocationFilter } from '../../../shared/locations/report-location-filter';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 58 -- the Inventory Variance Report: Book Balance (the accounting ledger) against Actual
 * Balance (the physical one) per product, listing only the products where they differ.
 *
 * The two ledgers diverge exactly where a Delivery Note or GRN has moved goods that no Invoice or
 * Purchase Bill has yet booked, or the reverse; the four shared types (Warehouse Transfer, Inventory
 * Adjustment, Production Journal, Opening Stock) write both and never open a gap. Difference is the
 * absolute gap, as the live column prints it, and the Remarks column carries its direction.
 *
 * Diverges from the live screen in three recorded ways (docs/phase-58-status.md): an as-of date
 * rather than a range, since a balance has no period; Category and Product filters rather than
 * "Group by Item/Category"; and it opens in both modes rather than refusing an Accounting tenant.
 */
@Component({
  selector: 'app-inventory-variance-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput, ReportLocationFilter, StatusBanner],
  templateUrl: './inventory-variance-page.html',
})
export class InventoryVariancePage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(CatalogueReportsService);
  private readonly catalogService = inject(CatalogService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  /** Phase 35b -- the Billing Location filter; empty is "All locations", the live default. */
  protected readonly locationId = signal('');

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<InventoryVarianceRowDto[]>([]);
  protected readonly categories = signal<ProductCategory[]>([]);
  protected readonly products = signal<Product[]>([]);

  /** The Nepal day, not the UTC one: an as-of balance read between 18:15 and 24:00 UTC would
   *  otherwise name yesterday (phase 48). */
  protected readonly asOfDate = signal(nepalToday());
  protected readonly categoryId = signal('');
  protected readonly productId = signal('');

  /** The tenant's Mode of Inventory Tracking, as the server reports it. The report reads both
   *  ledgers whatever it says; the note only tells an Accounting tenant why Actual may be empty. */
  protected readonly mode = signal<InventoryTrackingMode>('AccountingMovement');
  protected readonly accountingTenant = computed(() => this.mode() === 'AccountingMovement');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly exporting = signal(false);

  constructor() {
    this.catalogService.listProductCategories(this.organizationId).subscribe({
      next: (categories) => this.categories.set(categories),
    });
    this.catalogService.listAllProducts(this.organizationId).subscribe({
      next: (products) => this.products.set(products),
    });
    this.load();
  }

  protected remark(direction: InventoryVarianceDirection): string {
    return direction === 'ToBeShipped' ? 'Quantity To Be Shipped' : 'Quantity To Be Received';
  }

  protected onAsOfDateChange(value: string): void {
    this.asOfDate.set(value);
    this.reload();
  }

  protected onCategoryChange(event: Event): void {
    this.categoryId.set((event.target as HTMLSelectElement).value);
    this.reload();
  }

  protected onProductChange(event: Event): void {
    this.productId.set((event.target as HTMLSelectElement).value);
    this.reload();
  }

  protected onLocationChange(value: string): void {
    this.locationId.set(value);
    this.reload();
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

  protected exportCurrentView(): void {
    this.runExport(false, this.page(), this.pageSize());
  }

  protected exportFullDataset(): void {
    this.runExport(true, 1, this.pageSize());
  }

  private reload(): void {
    this.page.set(1);
    this.load();
  }

  private runExport(full: boolean, page: number, pageSize: number): void {
    this.exporting.set(true);
    this.reports
      .exportInventoryVariance(
        this.organizationId, this.asOfDate(), this.categoryId() || null, this.productId() || null,
        full, page, pageSize, this.locationId(),
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `InventoryVariance_${this.asOfDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Inventory Variance Report.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getInventoryVariance(
        this.organizationId, this.asOfDate(), this.categoryId() || null, this.productId() || null,
        this.page(), this.pageSize(), this.locationId(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          this.mode.set(report.mode);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Inventory Variance Report.');
        },
      });
  }
}
