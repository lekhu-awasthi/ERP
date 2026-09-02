import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { InventoryService } from '../../../core/inventory/inventory.service';
import { StockAgeingRowDto } from '../../../core/inventory/inventory.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, ProductCategory } from '../../../core/catalog/catalog.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';

/**
 * Read-only report screen -- Phase 19's StockAgeingQuery, the same 1-30/31-60/61-90/91+ day
 * buckets as Customer/Supplier Ageing Summary (decision #4), one row per Product.
 */
@Component({
  selector: 'app-stock-ageing-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput],
  templateUrl: './stock-ageing-page.html',
})
export class StockAgeingPage {
  private readonly route = inject(ActivatedRoute);
  private readonly inventoryService = inject(InventoryService);
  private readonly catalogService = inject(CatalogService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<StockAgeingRowDto[]>([]);
  protected readonly categories = signal<ProductCategory[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);

  protected readonly asOfDate = signal(this.today());
  protected readonly productCategoryId = signal('');
  protected readonly productId = signal('');
  protected readonly warehouseId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);
  protected readonly totalDays1To30 = signal(0);
  protected readonly totalDays31To60 = signal(0);
  protected readonly totalDays61To90 = signal(0);
  protected readonly totalDays91Plus = signal(0);
  protected readonly totalAmount = signal(0);

  protected readonly exporting = signal(false);

  constructor() {
    this.catalogService.listProductCategories(this.organizationId).subscribe({ next: (c) => this.categories.set(c) });
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({ next: (w) => this.warehouses.set(w) });
    this.load();
  }

  protected onAsOfDateChange(value: string): void {
    this.asOfDate.set(value);
    this.page.set(1);
    this.load();
  }

  protected onCategoryChange(event: Event): void {
    this.productCategoryId.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  protected onProductChange(event: Event): void {
    this.productId.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  protected onWarehouseChange(event: Event): void {
    this.warehouseId.set((event.target as HTMLSelectElement).value);
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

  protected exportCurrentView(): void {
    this.runExport(false, this.page(), this.pageSize());
  }

  protected exportFullDataset(): void {
    this.runExport(true, 1, this.pageSize());
  }

  private runExport(full: boolean, page: number, pageSize: number): void {
    this.exporting.set(true);
    this.inventoryService
      .exportStockAgeing(
        this.organizationId, this.asOfDate(), this.productCategoryId() || null, this.productId() || null,
        this.warehouseId() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `StockAgeing_${this.asOfDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export Stock Ageing.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.inventoryService
      .getStockAgeing(
        this.organizationId, this.asOfDate(), this.productCategoryId() || null, this.productId() || null,
        this.warehouseId() || null, this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          this.totalDays1To30.set(report.totalDays1To30);
          this.totalDays31To60.set(report.totalDays31To60);
          this.totalDays61To90.set(report.totalDays61To90);
          this.totalDays91Plus.set(report.totalDays91Plus);
          this.totalAmount.set(report.totalAmount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load Stock Ageing.');
        },
      });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
