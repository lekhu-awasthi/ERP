import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, ProductCategory } from '../../../core/catalog/catalog.models';
import { TradeByItemDto, TradeItemGrouping } from '../../../core/trade/trade-reports.models';
import { TradeReportsService } from '../../../core/trade/trade-reports.service';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';

/**
 * Sales By Item -- confirmed live 2026-09-03.
 *
 * The live Sales-side screen carries a "Filter By item/category" control whose two options are
 * literally Item and Category, switching each row between one product and one product category;
 * the Purchase-side screen has no such control. Both screens here expose it, because the backend
 * handler is shared and refusing the option on one side would be a difference without a reason.
 *
 * **Quantity has no total**, by design: the rows are products in different units of measure. The
 * live footer leaves that cell blank too (phase-26a's rule, reached independently).
 */
@Component({
  selector: 'app-sales-by-item-page',
  imports: [PaginationControl, AmountPipe, BsDateInput],
  templateUrl: './sales-by-item-page.html',
})
export class SalesByItemPage {
  private readonly route = inject(ActivatedRoute);
  private readonly catalog = inject(CatalogService);
  private readonly reports = inject(TradeReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<TradeByItemDto | null>(null);
  protected readonly categories = signal<ProductCategory[]>([]);
  protected readonly products = signal<Product[]>([]);

  protected readonly fromDate = signal(startOfYear());
  protected readonly toDate = signal(today());
  protected readonly groupBy = signal<TradeItemGrouping>('Item');
  protected readonly productCategoryId = signal('');
  protected readonly productId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.catalog.listProductCategories(this.organizationId).subscribe({ next: (c) => this.categories.set(c) });
    this.catalog.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
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

  protected onGroupByChange(event: Event): void {
    this.groupBy.set((event.target as HTMLSelectElement).value as TradeItemGrouping);
    this.reload();
  }

  protected onCategoryChange(event: Event): void {
    this.productCategoryId.set((event.target as HTMLSelectElement).value);
    this.reload();
  }

  protected onProductChange(event: Event): void {
    this.productId.set((event.target as HTMLSelectElement).value);
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
      .exportTradeByItem(
        this.organizationId, 'sales-by-item', this.fromDate(), this.toDate(), this.groupBy(),
        this.productCategoryId() || null, this.productId() || null, full, page, pageSize)
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `SalesByItem_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Sales By Item.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getTradeByItem(
        this.organizationId, 'sales-by-item', this.fromDate(), this.toDate(), this.groupBy(),
        this.productCategoryId() || null, this.productId() || null, this.page(), this.pageSize())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Sales By Item.');
        },
      });
  }
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}
