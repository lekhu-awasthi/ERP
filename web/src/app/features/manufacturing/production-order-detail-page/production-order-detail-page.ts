import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { CostTerm } from '../../../core/configuration/configuration.models';
import { ProductionOrderDetail, ProductionOrderRequest } from '../../../core/manufacturing/manufacturing.models';
import { ManufacturingService } from '../../../core/manufacturing/manufacturing.service';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { DocumentTabs } from '../../../shared/document-tabs/document-tabs';
import { ReportingTagsEditor } from '../../../shared/reporting-tags/reporting-tags-editor';
import { CustomFieldsEditor } from '../../../shared/custom-fields/custom-fields-editor';
import { commitCustomFieldsThen } from '../../../shared/custom-fields/commit-custom-fields';
import { PrintingService } from '../../../core/printing/printing.service';
import { openBlankTabForPrint, openBlobInNewTab } from '../../../shared/download-file';

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
 * The Production Order editor and detail view -- an <b>uncosted plan</b>. Raw materials carry a
 * quantity only (live-confirmed: that table has exactly two columns here), nothing touches stock or
 * the ledger, and there is no warehouse because nothing is moving yet.
 *
 * <p>The "Convert to Production Journal" banner disappears the moment the order has been converted,
 * because a second conversion is refused. That is a deliberate divergence: the reference product
 * still offers the button on an already-converted order.</p>
 */
@Component({
  selector: 'app-production-order-detail-page',
  imports: [RouterLink, DatePipe, AmountPipe, BsDateInput, DocumentTabs, ReportingTagsEditor, CustomFieldsEditor],
  templateUrl: './production-order-detail-page.html',
})
export class ProductionOrderDetailPage {
  /** Phase 27a: custom field values ride the document's own Save. See
   * commitCustomFieldsThen for why the commit is an rxjs operator rather than a
   * nested subscribe, and why a failed commit does not report the save as failed. */
  private readonly customFieldsEditor = viewChild(CustomFieldsEditor);

  private readonly route = inject(ActivatedRoute);
  private readonly printingService = inject(PrintingService);
  private readonly router = inject(Router);
  private readonly manufacturingService = inject(ManufacturingService);
  private readonly catalogService = inject(CatalogService);
  private readonly configurationService = inject(ConfigurationService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly loadingBom = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly warningMessage = signal<string | null>(null);
  protected readonly order = signal<ProductionOrderDetail | null>(null);
  protected readonly products = signal<Product[]>([]);
  protected readonly costTerms = signal<CostTerm[]>([]);
  protected readonly isNew = signal(false);

  protected readonly date = signal(this.today());
  protected readonly reference = signal('');
  protected readonly productId = signal('');
  protected readonly outputQuantity = signal(1);
  protected readonly notes = signal('');
  protected readonly rawMaterials = signal<EditableMaterial[]>([]);
  protected readonly byProducts = signal<EditableByProduct[]>([]);
  protected readonly expenses = signal<EditableExpense[]>([]);

  private billOfMaterialsId: string | null = null;
  protected readonly printing = signal(false);
  protected routeOrderId = '';

  protected readonly isDraft = computed(() => {
    const doc = this.order();
    return this.isNew() || !doc || doc.status === 'Draft';
  });

  protected readonly allocationTotal = computed(() =>
    this.byProducts().reduce((sum, line) => sum + (Number(line.costAllocationPct) || 0), 0),
  );

  protected readonly allocationIsSane = computed(() => this.allocationTotal() < 100);

  protected readonly canLoadBom = computed(() => !!this.productId() && this.outputQuantity() > 0 && this.isDraft());

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

  protected readonly canApprove = computed(() => !this.isNew() && this.isDraft() && this.canSave());

  protected readonly canConvert = computed(() => this.order()?.status === 'Approved');

  constructor() {
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.configurationService.listCostTerms(this.organizationId).subscribe({
      next: (terms) => this.costTerms.set(terms.filter((t) => t.category === 'ProductionCost' && t.isActive)),
    });

    this.route.paramMap.subscribe((params) => {
      this.routeOrderId = params.get('productionOrderId')!;
      const isNew = this.routeOrderId === 'new';
      this.isNew.set(isNew);
      this.order.set(null);
      this.errorMessage.set(null);
      this.warningMessage.set(null);

      if (isNew) {
        this.loading.set(false);
        this.date.set(this.today());
        this.reference.set('');
        this.productId.set('');
        this.outputQuantity.set(1);
        this.notes.set('');
        this.rawMaterials.set([{ key: nextKey++, productId: '', quantity: 1 }]);
        this.byProducts.set([]);
        this.expenses.set([]);
        this.billOfMaterialsId = null;
      } else {
        this.load();
      }
    });
  }

  protected onDate(value: string): void {
    this.date.set(value);
  }

  protected onReference(event: Event): void {
    this.reference.set((event.target as HTMLInputElement).value);
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

  protected loadBom(): void {
    if (!this.canLoadBom()) return;

    this.loadingBom.set(true);
    this.errorMessage.set(null);
    this.manufacturingService.getBomTemplate(this.organizationId, this.productId(), this.outputQuantity()).subscribe({
      next: (template) => {
        this.loadingBom.set(false);
        if (!template) {
          this.warningMessage.set('This product has no active bill of materials, so there was nothing to load.');
          return;
        }

        this.warningMessage.set(null);
        this.billOfMaterialsId = template.billOfMaterialsId;
        this.rawMaterials.set(
          template.rawMaterials.map((l) => ({ key: nextKey++, productId: l.productId, quantity: l.quantity })),
        );
        this.byProducts.set(
          template.byProducts.map((l) => ({
            key: nextKey++,
            productId: l.productId,
            quantity: l.quantity,
            costAllocationPct: l.costAllocationPct,
          })),
        );
        this.expenses.set(
          template.expenses.map((l) => ({ key: nextKey++, costTermId: l.costTermId, amount: l.amount })),
        );
      },
      error: (err: unknown) => {
        this.loadingBom.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the bill of materials.');
      },
    });
  }

  protected addMaterial(): void {
    this.rawMaterials.update((lines) => [...lines, { key: nextKey++, productId: '', quantity: 1 }]);
  }

  protected removeMaterial(key: number): void {
    this.rawMaterials.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected onMaterialProduct(key: number, event: Event): void {
    const productId = (event.target as HTMLSelectElement).value;
    this.rawMaterials.update((lines) => lines.map((l) => (l.key === key ? { ...l, productId } : l)));
  }

  protected onMaterialQuantity(key: number, event: Event): void {
    const quantity = Number((event.target as HTMLInputElement).value);
    this.rawMaterials.update((lines) => lines.map((l) => (l.key === key ? { ...l, quantity } : l)));
  }

  protected addByProduct(): void {
    this.byProducts.update((lines) => [...lines, { key: nextKey++, productId: '', quantity: 1, costAllocationPct: 0 }]);
  }

  protected removeByProduct(key: number): void {
    this.byProducts.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected onByProductProduct(key: number, event: Event): void {
    const productId = (event.target as HTMLSelectElement).value;
    this.byProducts.update((lines) => lines.map((l) => (l.key === key ? { ...l, productId } : l)));
  }

  protected onByProductQuantity(key: number, event: Event): void {
    const quantity = Number((event.target as HTMLInputElement).value);
    this.byProducts.update((lines) => lines.map((l) => (l.key === key ? { ...l, quantity } : l)));
  }

  protected onByProductPct(key: number, event: Event): void {
    const costAllocationPct = Number((event.target as HTMLInputElement).value);
    this.byProducts.update((lines) => lines.map((l) => (l.key === key ? { ...l, costAllocationPct } : l)));
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

  protected convert(): void {
    void this.router.navigate(
      ['/organizations', this.organizationId, 'manufacturing', 'production-journals', 'new'],
      { queryParams: { fromProductionOrder: this.routeOrderId } },
    );
  }

  protected save(): void {
    if (!this.canSave()) return;

    this.saving.set(true);
    this.errorMessage.set(null);

    const request: ProductionOrderRequest = {
      date: this.date(),
      reference: this.reference().trim() || null,
      productId: this.productId(),
      outputQuantity: this.outputQuantity(),
      billOfMaterialsId: this.billOfMaterialsId,
      notes: this.notes().trim() || null,
      rawMaterials: this.rawMaterials().map((l) => ({ productId: l.productId, quantity: l.quantity })),
      byProducts: this.byProducts().map((l) => ({
        productId: l.productId,
        costAllocationPct: l.costAllocationPct,
        quantity: l.quantity,
      })),
      expenses: this.expenses().map((l) => ({ costTermId: l.costTermId, amount: l.amount })),
    };

    if (this.isNew()) {
      this.manufacturingService.createProductionOrder(this.organizationId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), (r) => r.id, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: (result) => {
            this.saving.set(false);
            void this.router.navigate([
              '/organizations',
              this.organizationId,
              'manufacturing',
              'production-orders',
              result.id,
            ]);
          },
          error: (err: unknown) => this.fail(err, 'Could not create the production order.'),
        });
    } else {
      this.manufacturingService.updateProductionOrder(this.organizationId, this.routeOrderId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), () => this.routeOrderId, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: () => {
            this.saving.set(false);
            this.load();
          },
          error: (err: unknown) => this.fail(err, 'Could not save the production order.'),
        });
    }
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);
    this.manufacturingService.approveProductionOrder(this.organizationId, this.routeOrderId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve the production order.');
      },
    });
  }

  protected voidDocument(): void {
    this.voiding.set(true);
    this.errorMessage.set(null);
    this.manufacturingService.voidProductionOrder(this.organizationId, this.routeOrderId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void the production order.');
      },
    });
  }

  private fail(err: unknown, fallback: string): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }

  private load(): void {
    this.loading.set(true);
    this.manufacturingService.getProductionOrder(this.organizationId, this.routeOrderId).subscribe({
      next: (detail) => {
        this.order.set(detail);
        this.date.set(detail.date);
        this.reference.set(detail.reference ?? '');
        this.productId.set(detail.productId);
        this.outputQuantity.set(detail.outputQuantity);
        this.notes.set(detail.notes ?? '');
        this.billOfMaterialsId = detail.billOfMaterialsId;
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
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the production order.');
      },
    });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  /** Phase 27b -- print/PDF, wired for this document type alongside the other eight the phase
   * added. Opens the tab synchronously before the request so the browser attributes it to the
   * click rather than blocking it as a popup. */
  protected print(): void {
    this.printing.set(true);
    this.errorMessage.set(null);
    const tab = openBlankTabForPrint();

    this.printingService.printDocument(this.organizationId, 'ProductionOrder', this.routeOrderId).subscribe({
      next: (blob) => {
        this.printing.set(false);
        openBlobInNewTab(blob, tab);
      },
      error: (err: unknown) => {
        this.printing.set(false);
        tab?.close();
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print production order. Please try again.');
      },
    });
  }
}
