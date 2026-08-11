import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, ProductType } from '../../../core/catalog/catalog.models';

type ProductTypeFilter = ProductType | 'All';

/** List-page chrome for Product, mirroring contact-list-page's list->detail split. */
@Component({
  selector: 'app-product-list-page',
  imports: [RouterLink],
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

  protected readonly types: ProductTypeFilter[] = ['All', 'Goods', 'Service'];

  constructor() {
    this.load();
  }

  protected selectType(type: ProductTypeFilter): void {
    this.typeFilter.set(type);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    const type = this.typeFilter();
    this.catalogService.listProducts(this.organizationId, type === 'All' ? undefined : type).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load products.');
      },
    });
  }
}
