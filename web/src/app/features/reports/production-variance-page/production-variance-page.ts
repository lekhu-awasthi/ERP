import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { ProductionVarianceRow } from '../../../core/manufacturing/manufacturing.models';
import { ManufacturingService } from '../../../core/manufacturing/manufacturing.service';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Production Variance Report -- planned quantity (from the run's own BOM, scaled to its output)
 * against what the run actually used, per input and by-product. Only runs that carry a BOM appear:
 * there is nothing to vary against otherwise.
 */
@Component({
  selector: 'app-production-variance-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput, NepaliDatePipe],
  templateUrl: './production-variance-page.html',
})
export class ProductionVariancePage {
  private readonly route = inject(ActivatedRoute);
  private readonly manufacturingService = inject(ManufacturingService);
  private readonly catalogService = inject(CatalogService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(false);
  protected readonly generated = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<ProductionVarianceRow[]>([]);
  protected readonly products = signal<Product[]>([]);

  protected readonly fromDate = signal(this.startOfYear());
  protected readonly toDate = signal(this.today());
  protected readonly productId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  constructor() {
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
  }

  protected onProduct(event: Event): void {
    this.productId.set((event.target as HTMLSelectElement).value);
  }

  protected generate(): void {
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
    this.errorMessage.set(null);
    this.manufacturingService
      .productionVariance(
        this.organizationId,
        this.fromDate(),
        this.toDate(),
        this.productId() || undefined,
        this.page(),
        this.pageSize(),
      )
      .subscribe({
        next: (result) => {
          this.rows.set(result.items);
          this.totalCount.set(result.totalCount);
          this.generated.set(true);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not generate the production variance report.');
        },
      });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private startOfYear(): string {
    return `${new Date().getFullYear()}-01-01`;
  }
}
