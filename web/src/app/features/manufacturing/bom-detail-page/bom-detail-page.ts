import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { CostTerm } from '../../../core/configuration/configuration.models';
import { BillOfMaterialsDetail } from '../../../core/manufacturing/manufacturing.models';
import { ManufacturingService } from '../../../core/manufacturing/manufacturing.service';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';

interface EditableMaterial {
  key: number;
  productId: string;
  quantity: number;
}

interface EditableByProduct extends EditableMaterial {
  costAllocationPct: number;
}

interface EditableExpense {
  key: number;
  costTermId: string;
  amount: number;
}

let nextKey = 1;

/**
 * The BOM editor. Three line tables plus an Output Quantity, matching the reference product's own
 * form. Qty/Unit and Amount/Unit are shown but never entered -- they are the line divided by the
 * output quantity, so storing them would give one fact two homes.
 *
 * Handles both `.../new` and `.../:id` on one route, so the id is re-read from `paramMap` on every
 * emission rather than captured once from the snapshot (phase-3 bug #1: Angular reuses the
 * component instance across that navigation).
 */
@Component({
  selector: 'app-bom-detail-page',
  imports: [RouterLink, AmountPipe],
  templateUrl: './bom-detail-page.html',
})
export class BomDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly manufacturingService = inject(ManufacturingService);
  private readonly catalogService = inject(CatalogService);
  private readonly configurationService = inject(ConfigurationService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly deleting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly bom = signal<BillOfMaterialsDetail | null>(null);
  protected readonly products = signal<Product[]>([]);
  protected readonly costTerms = signal<CostTerm[]>([]);
  protected readonly isNew = signal(false);

  protected readonly productId = signal('');
  protected readonly outputQuantity = signal(1);
  protected readonly manufactureOnEverySale = signal(false);
  protected readonly notes = signal('');
  protected readonly isActive = signal(true);
  protected readonly rawMaterials = signal<EditableMaterial[]>([]);
  protected readonly byProducts = signal<EditableByProduct[]>([]);
  protected readonly expenses = signal<EditableExpense[]>([]);

  private routeBomId = '';

  /** The one invariant the server enforces, echoed here so the user sees it before saving. */
  protected readonly allocationTotal = computed(() =>
    this.byProducts().reduce((sum, line) => sum + (Number(line.costAllocationPct) || 0), 0),
  );

  protected readonly allocationIsSane = computed(() => this.allocationTotal() < 100);

  protected readonly canSave = computed(
    () =>
      !!this.productId() &&
      this.outputQuantity() > 0 &&
      this.rawMaterials().length > 0 &&
      this.rawMaterials().every((l) => l.productId && l.quantity > 0) &&
      this.byProducts().every((l) => l.productId && l.quantity > 0) &&
      this.expenses().every((l) => l.costTermId && l.amount >= 0) &&
      this.allocationIsSane(),
  );

  constructor() {
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.configurationService.listCostTerms(this.organizationId).subscribe({
      // Only Production Cost terms belong on a production form -- the category is a real
      // discriminator, not a display grouping (Phase 20c).
      next: (terms) => this.costTerms.set(terms.filter((t) => t.category === 'ProductionCost' && t.isActive)),
    });

    this.route.paramMap.subscribe((params) => {
      this.routeBomId = params.get('bomId')!;
      const isNew = this.routeBomId === 'new';
      this.isNew.set(isNew);
      this.bom.set(null);
      this.errorMessage.set(null);

      if (isNew) {
        this.loading.set(false);
        this.productId.set('');
        this.outputQuantity.set(1);
        this.manufactureOnEverySale.set(false);
        this.notes.set('');
        this.isActive.set(true);
        this.rawMaterials.set([this.newMaterial()]);
        this.byProducts.set([]);
        this.expenses.set([]);
      } else {
        this.load();
      }
    });
  }

  protected perUnit(quantity: number): number {
    const output = this.outputQuantity();
    return output > 0 ? quantity / output : 0;
  }

  protected onProductId(event: Event): void {
    this.productId.set((event.target as HTMLSelectElement).value);
  }

  protected onOutputQuantity(event: Event): void {
    this.outputQuantity.set(Number((event.target as HTMLInputElement).value));
  }

  protected onNotes(event: Event): void {
    this.notes.set((event.target as HTMLTextAreaElement).value);
  }

  protected onManufactureOnEverySale(event: Event): void {
    this.manufactureOnEverySale.set((event.target as HTMLInputElement).checked);
  }

  protected onIsActive(event: Event): void {
    this.isActive.set((event.target as HTMLInputElement).checked);
  }

  protected addMaterial(): void {
    this.rawMaterials.update((lines) => [...lines, this.newMaterial()]);
  }

  protected removeMaterial(key: number): void {
    this.rawMaterials.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected updateMaterial(key: number, patch: Partial<EditableMaterial>): void {
    this.rawMaterials.update((lines) => lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  }

  protected onMaterialProduct(key: number, event: Event): void {
    this.updateMaterial(key, { productId: (event.target as HTMLSelectElement).value });
  }

  protected onMaterialQuantity(key: number, event: Event): void {
    this.updateMaterial(key, { quantity: Number((event.target as HTMLInputElement).value) });
  }

  protected addByProduct(): void {
    this.byProducts.update((lines) => [...lines, { key: nextKey++, productId: '', quantity: 1, costAllocationPct: 0 }]);
  }

  protected removeByProduct(key: number): void {
    this.byProducts.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected updateByProduct(key: number, patch: Partial<EditableByProduct>): void {
    this.byProducts.update((lines) => lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  }

  protected onByProductProduct(key: number, event: Event): void {
    this.updateByProduct(key, { productId: (event.target as HTMLSelectElement).value });
  }

  protected onByProductQuantity(key: number, event: Event): void {
    this.updateByProduct(key, { quantity: Number((event.target as HTMLInputElement).value) });
  }

  protected onByProductPct(key: number, event: Event): void {
    this.updateByProduct(key, { costAllocationPct: Number((event.target as HTMLInputElement).value) });
  }

  protected addExpense(): void {
    this.expenses.update((lines) => [...lines, { key: nextKey++, costTermId: '', amount: 0 }]);
  }

  protected removeExpense(key: number): void {
    this.expenses.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected onExpenseTerm(key: number, event: Event): void {
    const costTermId = (event.target as HTMLSelectElement).value;
    this.expenses.update((lines) => lines.map((l) => (l.key === key ? { ...l, costTermId } : l)));
  }

  protected onExpenseAmount(key: number, event: Event): void {
    const amount = Number((event.target as HTMLInputElement).value);
    this.expenses.update((lines) => lines.map((l) => (l.key === key ? { ...l, amount } : l)));
  }

  protected save(): void {
    if (!this.canSave()) return;

    this.saving.set(true);
    this.errorMessage.set(null);

    const request = {
      productId: this.productId(),
      outputQuantity: this.outputQuantity(),
      manufactureOnEverySale: this.manufactureOnEverySale(),
      notes: this.notes().trim() || null,
      isActive: this.isActive(),
      rawMaterials: this.rawMaterials().map((l) => ({ productId: l.productId, quantity: l.quantity })),
      byProducts: this.byProducts().map((l) => ({
        productId: l.productId,
        costAllocationPct: l.costAllocationPct,
        quantity: l.quantity,
      })),
      expenses: this.expenses().map((l) => ({ costTermId: l.costTermId, amount: l.amount })),
    };

    // Explicit branch rather than a shared request$ variable -- the two result types differ and a
    // ternary makes .subscribe() unresolvable (phase-4 bug #3).
    if (this.isNew()) {
      this.manufacturingService.createBillOfMaterials(this.organizationId, request).subscribe({
        next: (result) => {
          this.saving.set(false);
          void this.router.navigate([
            '/organizations',
            this.organizationId,
            'manufacturing',
            'bills-of-materials',
            result.id,
          ]);
        },
        error: (err: unknown) => this.fail(err, 'Could not create the bill of materials.'),
      });
    } else {
      this.manufacturingService.updateBillOfMaterials(this.organizationId, this.routeBomId, request).subscribe({
        next: () => {
          this.saving.set(false);
          this.load();
        },
        error: (err: unknown) => this.fail(err, 'Could not save the bill of materials.'),
      });
    }
  }

  protected remove(): void {
    this.deleting.set(true);
    this.errorMessage.set(null);
    this.manufacturingService.deleteBillOfMaterials(this.organizationId, this.routeBomId).subscribe({
      next: () => {
        this.deleting.set(false);
        void this.router.navigate(['/organizations', this.organizationId, 'manufacturing', 'bills-of-materials']);
      },
      error: (err: unknown) => {
        this.deleting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete the bill of materials.');
      },
    });
  }

  private fail(err: unknown, fallback: string): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }

  private newMaterial(): EditableMaterial {
    return { key: nextKey++, productId: '', quantity: 1 };
  }

  private load(): void {
    this.loading.set(true);
    this.manufacturingService.getBillOfMaterials(this.organizationId, this.routeBomId).subscribe({
      next: (detail) => {
        this.bom.set(detail);
        this.productId.set(detail.productId);
        this.outputQuantity.set(detail.outputQuantity);
        this.manufactureOnEverySale.set(detail.manufactureOnEverySale);
        this.notes.set(detail.notes ?? '');
        this.isActive.set(detail.isActive);
        this.rawMaterials.set(
          detail.rawMaterials.map((l) => ({ key: nextKey++, productId: l.productId, quantity: l.quantity })),
        );
        this.byProducts.set(
          detail.byProducts.map((l) => ({
            key: nextKey++,
            productId: l.productId,
            quantity: l.quantity,
            costAllocationPct: l.costAllocationPct,
          })),
        );
        this.expenses.set(
          detail.expenses.map((l) => ({ key: nextKey++, costTermId: l.costTermId, amount: l.amount })),
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the bill of materials.');
      },
    });
  }
}
