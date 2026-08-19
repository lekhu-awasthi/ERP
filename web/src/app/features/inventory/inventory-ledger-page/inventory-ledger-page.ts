import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { InventoryService } from '../../../core/inventory/inventory.service';
import { InventoryLedgerRowDto } from '../../../core/inventory/inventory.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';

/** Read-only report screen -- the kardex view (architecture-spec.md §4.3's InventoryLedgerQuery),
 * chronological movements with a running balance for one Product+Warehouse. Both are required,
 * so this page shows the two selects until both are picked (either by hand, or pre-filled via
 * query params from stock-position-page's "View Ledger" link). */
@Component({
  selector: 'app-inventory-ledger-page',
  imports: [RouterLink],
  templateUrl: './inventory-ledger-page.html',
})
export class InventoryLedgerPage {
  private readonly route = inject(ActivatedRoute);
  private readonly inventoryService = inject(InventoryService);
  private readonly catalogService = inject(CatalogService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<InventoryLedgerRowDto[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);

  protected readonly productId = signal('');
  protected readonly warehouseId = signal('');

  constructor() {
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({ next: (w) => this.warehouses.set(w) });

    this.route.queryParamMap.subscribe((params) => {
      this.productId.set(params.get('productId') ?? '');
      this.warehouseId.set(params.get('warehouseId') ?? '');
      this.load();
    });
  }

  protected productLabel(productId: string): string {
    const product = this.products().find((p) => p.id === productId);
    return product ? `${product.code} — ${product.name}` : '—';
  }

  protected onProductChange(event: Event): void {
    this.productId.set((event.target as HTMLSelectElement).value);
    this.load();
  }

  protected onWarehouseChange(event: Event): void {
    this.warehouseId.set((event.target as HTMLSelectElement).value);
    this.load();
  }

  private load(): void {
    if (!this.productId() || !this.warehouseId()) {
      this.rows.set([]);
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);

    this.inventoryService.getInventoryLedger(this.organizationId, this.productId(), this.warehouseId()).subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load inventory ledger.');
      },
    });
  }
}
