import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { InventoryService } from '../../../core/inventory/inventory.service';
import { StockPositionDto } from '../../../core/inventory/inventory.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';

/** Read-only report screen, not a document form -- architecture-spec.md §4.3's
 * ProductStockPositionQuery, Opening/In/Out/Balance per (Product, Warehouse), optionally
 * filtered to one Product and/or one Warehouse. */
@Component({
  selector: 'app-stock-position-page',
  imports: [RouterLink],
  templateUrl: './stock-position-page.html',
})
export class StockPositionPage {
  private readonly route = inject(ActivatedRoute);
  private readonly inventoryService = inject(InventoryService);
  private readonly catalogService = inject(CatalogService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly positions = signal<StockPositionDto[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);

  protected readonly productFilter = signal('');
  protected readonly warehouseFilter = signal('');

  constructor() {
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({ next: (w) => this.warehouses.set(w) });
    this.load();
  }

  protected productLabel(productId: string): string {
    const product = this.products().find((p) => p.id === productId);
    return product ? `${product.code} — ${product.name}` : '—';
  }

  protected warehouseLabel(warehouseId: string): string {
    const warehouse = this.warehouses().find((w) => w.id === warehouseId);
    return warehouse ? warehouse.name : '—';
  }

  protected onProductFilterChange(event: Event): void {
    this.productFilter.set((event.target as HTMLSelectElement).value);
    this.load();
  }

  protected onWarehouseFilterChange(event: Event): void {
    this.warehouseFilter.set((event.target as HTMLSelectElement).value);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.inventoryService
      .getStockPosition(this.organizationId, this.productFilter() || undefined, this.warehouseFilter() || undefined)
      .subscribe({
        next: (positions) => {
          this.positions.set(positions);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load stock position.');
        },
      });
  }
}
