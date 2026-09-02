import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { ProductionSummaryRow, ProductionSummaryTotals } from '../../../core/manufacturing/manufacturing.models';
import { ManufacturingService } from '../../../core/manufacturing/manufacturing.service';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Production Summary Report, whose column blocks were read off the live report: Finished Goods
 * Produced / Raw Material Consumed / By Product Produced / Production Expenses.
 *
 * <p>The footer totals come from the server over the <b>full filtered set</b>, never a reduce over
 * the current page -- phase-16c bug #1 found four report pages doing exactly that.</p>
 */
@Component({
  selector: 'app-production-summary-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput, NepaliDatePipe],
  templateUrl: './production-summary-page.html',
})
export class ProductionSummaryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly manufacturingService = inject(ManufacturingService);
  private readonly catalogService = inject(CatalogService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(false);
  protected readonly generated = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<ProductionSummaryRow[]>([]);
  protected readonly totals = signal<ProductionSummaryTotals | null>(null);
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
      .productionSummary(
        this.organizationId,
        this.fromDate(),
        this.toDate(),
        this.productId() || undefined,
        this.page(),
        this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.rows.items);
          this.totalCount.set(report.rows.totalCount);
          this.totals.set(report.totals);
          this.generated.set(true);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not generate the production summary report.');
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
