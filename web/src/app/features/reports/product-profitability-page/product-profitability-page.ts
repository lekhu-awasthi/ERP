import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { InventoryService } from '../../../core/inventory/inventory.service';
import { ProductProfitabilityRowDto } from '../../../core/inventory/inventory.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, ProductCategory } from '../../../core/catalog/catalog.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Read-only report screen -- Phase 19's ProductProfitabilityQuery, a per-product-per-period
 * aggregate (decision #5). Production Cost/Consumption/Additional Cost always show 0 --
 * Manufacturing and Cost Terms/landed-cost are unbuilt (see phase-19-status.md's known limitations).
 */
@Component({
  selector: 'app-product-profitability-page',
  imports: [RouterLink, PaginationControl],
  templateUrl: './product-profitability-page.html',
})
export class ProductProfitabilityPage {
  private readonly route = inject(ActivatedRoute);
  private readonly inventoryService = inject(InventoryService);
  private readonly catalogService = inject(CatalogService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<ProductProfitabilityRowDto[]>([]);
  protected readonly categories = signal<ProductCategory[]>([]);
  protected readonly products = signal<Product[]>([]);

  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());
  protected readonly productCategoryId = signal('');
  protected readonly productId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);
  protected readonly totalSales = signal(0);
  protected readonly totalCostOfSales = signal(0);
  protected readonly totalGrossProfit = signal(0);

  protected readonly exporting = signal(false);

  constructor() {
    this.catalogService.listProductCategories(this.organizationId).subscribe({ next: (c) => this.categories.set(c) });
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.load();
  }

  protected onFromDateChange(event: Event): void {
    this.fromDate.set((event.target as HTMLInputElement).value);
    this.page.set(1);
    this.load();
  }

  protected onToDateChange(event: Event): void {
    this.toDate.set((event.target as HTMLInputElement).value);
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
      .exportProductProfitability(
        this.organizationId, this.fromDate(), this.toDate(), this.productCategoryId() || null,
        this.productId() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `ProductProfitability_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export Product Profitability.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.inventoryService
      .getProductProfitability(
        this.organizationId, this.fromDate(), this.toDate(), this.productCategoryId() || null,
        this.productId() || null, this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          this.totalSales.set(report.totalSales);
          this.totalCostOfSales.set(report.totalCostOfSales);
          this.totalGrossProfit.set(report.totalGrossProfit);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load Product Profitability.');
        },
      });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private firstOfMonth(): string {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
  }
}
