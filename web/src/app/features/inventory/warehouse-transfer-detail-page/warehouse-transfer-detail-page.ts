import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { InventoryService } from '../../../core/inventory/inventory.service';
import { WarehouseTransferDetail, WarehouseTransferLineInput } from '../../../core/inventory/inventory.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';

interface EditableLine {
  key: number;
  productId: string;
  quantity: number;
}

let nextLineKey = 1;

/** Clones purchase-bill-detail-page's chrome, minus GL/TDS/import fields -- WarehouseTransfer
 * never posts GL (see the aggregate's own doc comment), so there's no Preview GL Posting button
 * or GL Transactions section here, unlike every other transactional detail page. */
@Component({
  selector: 'app-warehouse-transfer-detail-page',
  imports: [RouterLink, DatePipe],
  templateUrl: './warehouse-transfer-detail-page.html',
})
export class WarehouseTransferDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly inventoryService = inject(InventoryService);
  private readonly catalogService = inject(CatalogService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly warehouseTransfer = signal<WarehouseTransferDetail | null>(null);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly isNew = signal(false);

  protected readonly fromWarehouseId = signal('');
  protected readonly toWarehouseId = signal('');
  protected readonly date = signal(this.today());
  protected readonly reference = signal('');
  protected readonly lines = signal<EditableLine[]>([]);

  private routeWarehouseTransferId = '';

  protected readonly isDraft = computed(() => {
    const doc = this.warehouseTransfer();
    return this.isNew() || !doc || doc.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew()
      && lines.length >= 1
      && lines.every((l) => l.productId && l.quantity > 0)
      && !!this.fromWarehouseId()
      && !!this.toWarehouseId()
      && this.fromWarehouseId() !== this.toWarehouseId();
  });

  constructor() {
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({ next: (w) => this.warehouses.set(w) });

    this.route.paramMap.subscribe((params) => {
      this.routeWarehouseTransferId = params.get('warehouseTransferId')!;
      const isNew = this.routeWarehouseTransferId === 'new';
      this.isNew.set(isNew);
      this.warehouseTransfer.set(null);
      this.errorMessage.set(null);

      if (isNew) {
        this.loading.set(false);
        this.fromWarehouseId.set('');
        this.toWarehouseId.set('');
        this.date.set(this.today());
        this.reference.set('');
        this.lines.set([this.newLine()]);
      } else {
        this.load();
      }
    });
  }

  protected productLabel(productId: string): string {
    const product = this.products().find((p) => p.id === productId);
    return product ? `${product.code} — ${product.name}` : '—';
  }

  protected warehouseLabel(warehouseId: string): string {
    const warehouse = this.warehouses().find((w) => w.id === warehouseId);
    return warehouse ? warehouse.name : '—';
  }

  protected onProductChange(key: number, event: Event): void {
    const productId = (event.target as HTMLSelectElement).value;
    this.updateLine(key, { productId });
  }

  protected onQuantityChange(key: number, event: Event): void {
    const quantity = (event.target as HTMLInputElement).valueAsNumber;
    this.updateLine(key, { quantity: Number.isFinite(quantity) ? quantity : 0 });
  }

  protected addLine(): void {
    this.lines.update((lines) => [...lines, this.newLine()]);
  }

  protected removeLine(key: number): void {
    this.lines.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected saveDraft(): void {
    if (!this.fromWarehouseId() || !this.toWarehouseId()) {
      this.errorMessage.set('Select both a From Warehouse and a To Warehouse.');
      return;
    }
    if (this.fromWarehouseId() === this.toWarehouseId()) {
      this.errorMessage.set('From Warehouse and To Warehouse must differ.');
      return;
    }

    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const request = {
      fromWarehouseId: this.fromWarehouseId(),
      toWarehouseId: this.toWarehouseId(),
      date: this.date(),
      reference: this.reference() || null,
      lines,
    };

    if (this.isNew()) {
      this.inventoryService.createWarehouseTransfer(this.organizationId, request).subscribe({
        next: (result) => {
          this.saving.set(false);
          this.router.navigate(['/organizations', this.organizationId, 'inventory', 'warehouse-transfers', result.id]);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save warehouse transfer. Please try again.');
        },
      });
    } else {
      this.inventoryService.updateWarehouseTransfer(this.organizationId, this.routeWarehouseTransferId, request).subscribe({
        next: () => {
          this.saving.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save warehouse transfer. Please try again.');
        },
      });
    }
  }

  protected voidWarehouseTransfer(): void {
    if (!window.confirm('Void this warehouse transfer? This restocks the source warehouse and cannot be undone; it will be rejected if the moved stock has already left the destination warehouse.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.inventoryService.voidWarehouseTransfer(this.organizationId, this.routeWarehouseTransferId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void warehouse transfer. Please try again.');
      },
    });
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.inventoryService.approveWarehouseTransfer(this.organizationId, this.routeWarehouseTransferId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve warehouse transfer. Please try again.');
      },
    });
  }

  private toLineInputs(): WarehouseTransferLineInput[] | null {
    const lines = this.lines()
      .filter((l) => l.productId && l.quantity > 0)
      .map((l) => ({ productId: l.productId, quantity: l.quantity }));

    if (lines.length === 0) {
      this.errorMessage.set('Add at least one line with a Product and a Quantity.');
      return null;
    }

    return lines;
  }

  private updateLine(key: number, patch: Partial<Omit<EditableLine, 'key'>>): void {
    this.lines.update((lines) => lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  }

  private newLine(): EditableLine {
    return { key: nextLineKey++, productId: '', quantity: 1 };
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private load(): void {
    this.loading.set(true);
    this.inventoryService.getWarehouseTransfer(this.organizationId, this.routeWarehouseTransferId).subscribe({
      next: (doc) => {
        this.warehouseTransfer.set(doc);
        this.fromWarehouseId.set(doc.fromWarehouseId);
        this.toWarehouseId.set(doc.toWarehouseId);
        this.date.set(doc.date);
        this.reference.set(doc.reference ?? '');
        this.lines.set(
          doc.lines.length > 0
            ? doc.lines.map((l) => ({ key: nextLineKey++, productId: l.productId, quantity: l.quantity }))
            : [this.newLine()],
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load warehouse transfer.');
      },
    });
  }
}
