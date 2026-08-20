import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, ProductType } from '../../../core/catalog/catalog.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';

type ProductTypeFilter = ProductType | 'All';

/** List-page chrome for Product, mirroring contact-list-page's list->detail split. */
@Component({
  selector: 'app-product-list-page',
  imports: [RouterLink, PaginationControl],
  templateUrl: './product-list-page.html',
})
export class ProductListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly catalogService = inject(CatalogService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<Product[]>([]);
  protected readonly typeFilter = signal<ProductTypeFilter>('All');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly types: ProductTypeFilter[] = ['All', 'Goods', 'Service'];

  constructor() {
    this.load();
  }

  protected selectType(type: ProductTypeFilter): void {
    this.typeFilter.set(type);
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
    const type = this.typeFilter();
    this.catalogService
      .listProducts(this.organizationId, type === 'All' ? undefined : type, this.page(), this.pageSize())
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load products.');
        },
      });
  }
}
