import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';
import { ProductionPlanningReport } from '../../../core/manufacturing/manufacturing.models';
import { ManufacturingService } from '../../../core/manufacturing/manufacturing.service';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';

/**
 * Production Planning Report -- <b>not</b> a period report. Pick a product and a quantity to make,
 * and it explodes that product's bill of materials and compares each input against stock on hand.
 * Single-level, as the live report's own "Multiple Level: No" header states.
 */
@Component({
  selector: 'app-production-planning-page',
  imports: [RouterLink, AmountPipe],
  templateUrl: './production-planning-page.html',
})
export class ProductionPlanningPage {
  private readonly route = inject(ActivatedRoute);
  private readonly manufacturingService = inject(ManufacturingService);
  private readonly catalogService = inject(CatalogService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<ProductionPlanningReport | null>(null);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);

  protected readonly productId = signal('');
  protected readonly quantity = signal(1);
  protected readonly warehouseId = signal('');

  protected readonly canGenerate = computed(() => !!this.productId() && this.quantity() > 0);

  constructor() {
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({ next: (w) => this.warehouses.set(w) });
  }

  protected onProduct(event: Event): void {
    this.productId.set((event.target as HTMLSelectElement).value);
  }

  protected onWarehouse(event: Event): void {
    this.warehouseId.set((event.target as HTMLSelectElement).value);
  }

  protected onQuantity(event: Event): void {
    this.quantity.set(Number((event.target as HTMLInputElement).value));
  }

  protected generate(): void {
    if (!this.canGenerate()) return;

    this.loading.set(true);
    this.errorMessage.set(null);
    this.manufacturingService
      .productionPlanning(this.organizationId, this.productId(), this.quantity(), this.warehouseId() || undefined)
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not generate the production planning report.');
        },
      });
  }
}
