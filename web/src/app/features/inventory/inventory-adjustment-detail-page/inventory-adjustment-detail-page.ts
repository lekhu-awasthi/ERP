import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { InventoryService } from '../../../core/inventory/inventory.service';
import {
  InventoryAdjustmentDetail,
  InventoryAdjustmentDirection,
  InventoryAdjustmentLineInput,
} from '../../../core/inventory/inventory.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';

interface EditableLine {
  key: number;
  productId: string;
  direction: InventoryAdjustmentDirection;
  quantity: number;
  unitCost: number;
}

let nextLineKey = 1;

/** Clones warehouse-transfer-detail-page's chrome (single Warehouse, no TDS/import), plus a
 * per-line Direction select and a Unit Cost input that's only meaningful (and only editable) for
 * Increase lines -- see InventoryAdjustmentLine's doc comment for why Decrease never carries its
 * own cost. Unlike WarehouseTransfer, this DOES post GL, so it shows the read-only GL Transactions
 * section once Approved, same as every GL-posting document type. */
@Component({
  selector: 'app-inventory-adjustment-detail-page',
  imports: [RouterLink, DatePipe, AmountPipe, BsDateInput],
  templateUrl: './inventory-adjustment-detail-page.html',
})
export class InventoryAdjustmentDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly inventoryService = inject(InventoryService);
  private readonly catalogService = inject(CatalogService);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly inventoryAdjustment = signal<InventoryAdjustmentDetail | null>(null);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly isNew = signal(false);

  protected readonly warehouseId = signal('');
  protected readonly date = signal(this.today());
  protected readonly reference = signal('');
  protected readonly lines = signal<EditableLine[]>([]);

  protected readonly directions: InventoryAdjustmentDirection[] = ['Increase', 'Decrease'];

  private routeInventoryAdjustmentId = '';

  protected readonly isDraft = computed(() => {
    const doc = this.inventoryAdjustment();
    return this.isNew() || !doc || doc.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew()
      && lines.length >= 1
      && lines.every((l) => l.productId && l.quantity > 0 && (l.direction === 'Decrease' || l.unitCost >= 0))
      && !!this.warehouseId();
  });

  constructor() {
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({ next: (w) => this.warehouses.set(w) });
    this.accountingService.listAllAccounts(this.organizationId).subscribe({ next: (a) => this.accounts.set(a) });

    this.route.paramMap.subscribe((params) => {
      this.routeInventoryAdjustmentId = params.get('inventoryAdjustmentId')!;
      const isNew = this.routeInventoryAdjustmentId === 'new';
      this.isNew.set(isNew);
      this.inventoryAdjustment.set(null);
      this.errorMessage.set(null);

      if (isNew) {
        this.loading.set(false);
        this.warehouseId.set('');
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

  protected accountLabel(accountId: string): string {
    const account = this.accounts().find((a) => a.id === accountId);
    return account ? `${account.code} — ${account.name}` : '—';
  }

  protected onProductChange(key: number, event: Event): void {
    const productId = (event.target as HTMLSelectElement).value;
    this.updateLine(key, { productId });
  }

  protected onDirectionChange(key: number, event: Event): void {
    const direction = (event.target as HTMLSelectElement).value as InventoryAdjustmentDirection;
    this.updateLine(key, { direction, unitCost: direction === 'Increase' ? this.lines().find((l) => l.key === key)?.unitCost ?? 0 : 0 });
  }

  protected onQuantityChange(key: number, event: Event): void {
    const quantity = (event.target as HTMLInputElement).valueAsNumber;
    this.updateLine(key, { quantity: Number.isFinite(quantity) ? quantity : 0 });
  }

  protected onUnitCostChange(key: number, event: Event): void {
    const unitCost = (event.target as HTMLInputElement).valueAsNumber;
    this.updateLine(key, { unitCost: Number.isFinite(unitCost) ? unitCost : 0 });
  }

  protected addLine(): void {
    this.lines.update((lines) => [...lines, this.newLine()]);
  }

  protected removeLine(key: number): void {
    this.lines.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected saveDraft(): void {
    if (!this.warehouseId()) {
      this.errorMessage.set('Select a Warehouse.');
      return;
    }

    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const request = {
      warehouseId: this.warehouseId(),
      date: this.date(),
      reference: this.reference() || null,
      lines,
    };

    if (this.isNew()) {
      this.inventoryService.createInventoryAdjustment(this.organizationId, request).subscribe({
        next: (result) => {
          this.saving.set(false);
          this.router.navigate(['/organizations', this.organizationId, 'inventory', 'inventory-adjustments', result.id]);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save inventory adjustment. Please try again.');
        },
      });
    } else {
      this.inventoryService.updateInventoryAdjustment(this.organizationId, this.routeInventoryAdjustmentId, request).subscribe({
        next: () => {
          this.saving.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save inventory adjustment. Please try again.');
        },
      });
    }
  }

  protected voidInventoryAdjustment(): void {
    if (!window.confirm('Void this inventory adjustment? This reverses its GL posting and stock effect, and cannot be undone; it will be rejected if any increased stock has already left the warehouse.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.inventoryService.voidInventoryAdjustment(this.organizationId, this.routeInventoryAdjustmentId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void inventory adjustment. Please try again.');
      },
    });
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.inventoryService.approveInventoryAdjustment(this.organizationId, this.routeInventoryAdjustmentId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve inventory adjustment. Please try again.');
      },
    });
  }

  private toLineInputs(): InventoryAdjustmentLineInput[] | null {
    const lines = this.lines()
      .filter((l) => l.productId && l.quantity > 0)
      .map((l) => ({ productId: l.productId, direction: l.direction, quantity: l.quantity, unitCost: l.unitCost }));

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
    return { key: nextLineKey++, productId: '', direction: 'Increase', quantity: 1, unitCost: 0 };
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private load(): void {
    this.loading.set(true);
    this.inventoryService.getInventoryAdjustment(this.organizationId, this.routeInventoryAdjustmentId).subscribe({
      next: (doc) => {
        this.inventoryAdjustment.set(doc);
        this.warehouseId.set(doc.warehouseId);
        this.date.set(doc.date);
        this.reference.set(doc.reference ?? '');
        this.lines.set(
          doc.lines.length > 0
            ? doc.lines.map((l) => ({
                key: nextLineKey++,
                productId: l.productId,
                direction: l.direction,
                quantity: l.quantity,
                unitCost: l.unitCost,
              }))
            : [this.newLine()],
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load inventory adjustment.');
      },
    });
  }
}
