import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import {
  InventoryBalanceFilter,
  InventoryPositionRowDto,
} from '../../../core/reports/catalogue-reports.models';
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
 * Phase 26c -- Inventory Position: quantity, rate and value per product as at the period end.
 *
 * These are Inventory Movement's Balance columns exactly; both screens read one server-side
 * `StockFactReader`, so the two cannot disagree.
 */
@Component({
  selector: 'app-inventory-position-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput],
  templateUrl: './inventory-position-page.html',
})
export class InventoryPositionPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(CatalogueReportsService);
  private readonly catalogService = inject(CatalogService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<InventoryPositionRowDto[]>([]);
  protected readonly categories = signal<ProductCategory[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);

  protected readonly fromDate = signal(firstOfMonth());
  protected readonly toDate = signal(today());
  protected readonly categoryId = signal('');
  protected readonly productId = signal('');
  protected readonly warehouseId = signal('');
  protected readonly balanceFilter = signal<InventoryBalanceFilter>('All');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);
  protected readonly totalQuantity = signal(0);
  protected readonly totalAmount = signal(0);

  protected readonly exporting = signal(false);

  constructor() {
    this.catalogService.listProductCategories(this.organizationId).subscribe({
      next: (categories) => this.categories.set(categories),
    });
    this.catalogService.listAllProducts(this.organizationId).subscribe({
      next: (products) => this.products.set(products),
    });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({
      next: (warehouses) => this.warehouses.set(warehouses),
    });
    this.load();
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.reload();
  }

  protected onToDateChange(value: string): void {
    this.toDate.set(value);
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

  protected onWarehouseChange(event: Event): void {
    this.warehouseId.set((event.target as HTMLSelectElement).value);
    this.reload();
  }

  protected onBalanceFilterChange(event: Event): void {
    this.balanceFilter.set((event.target as HTMLSelectElement).value as InventoryBalanceFilter);
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
      .exportInventoryPosition(
        this.organizationId, this.fromDate(), this.toDate(), this.categoryId() || null,
        this.productId() || null, this.warehouseId() || null, this.balanceFilter(), full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `InventoryPosition_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export Inventory Position.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getInventoryPosition(
        this.organizationId, this.fromDate(), this.toDate(), this.categoryId() || null,
        this.productId() || null, this.warehouseId() || null, this.balanceFilter(),
        this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          this.totalQuantity.set(report.totalQuantity);
          this.totalAmount.set(report.totalAmount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load Inventory Position.');
        },
      });
  }
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function firstOfMonth(): string {
  const now = new Date();
  return new Date(Date.UTC(now.getFullYear(), now.getMonth(), 1)).toISOString().slice(0, 10);
}
