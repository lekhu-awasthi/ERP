import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { CostTerm } from '../../../core/configuration/configuration.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';
import { ProductionJournalDetail, ProductionJournalRequest } from '../../../core/manufacturing/manufacturing.models';
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
 * The Production Journal editor and detail view.
 *
 * <p>Raw-material lines carry a Quantity and <b>no rate</b>: a Draft simply does not know what the
 * run will cost, because the cost is whatever FIFO layers Approve actually walks. The reference
 * product offers an editable Rate here, pre-filled from stock; we do not, so the document can never
 * claim a cost the ledger did not give up. The six-figure roll-up box therefore appears only once
 * the journal is Approved.</p>
 *
 * <p>No Bootstrap JS anywhere (it is not loaded in this app), no `[value]` on a signal-fed
 * `<select>` (the app is zoneless and the binding loses to `@for`-generated options), and no
 * `FormControl.value` read inside a `computed()`.</p>
 */
@Component({
  selector: 'app-production-journal-detail-page',
  imports: [RouterLink, DatePipe, AmountPipe, BsDateInput, DocumentTabs, ReportingTagsEditor, CustomFieldsEditor],
  templateUrl: './production-journal-detail-page.html',
})
export class ProductionJournalDetailPage {
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
  private readonly organizationsService = inject(OrganizationsService);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly loadingBom = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly warningMessage = signal<string | null>(null);
  protected readonly journal = signal<ProductionJournalDetail | null>(null);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly costTerms = signal<CostTerm[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly isNew = signal(false);

  protected readonly date = signal(this.today());
  protected readonly reference = signal('');
  protected readonly productId = signal('');
  protected readonly outputQuantity = signal(1);
  protected readonly warehouseId = signal('');
  protected readonly notes = signal('');
  protected readonly rawMaterials = signal<EditableMaterial[]>([]);
  protected readonly byProducts = signal<EditableByProduct[]>([]);
  protected readonly expenses = signal<EditableExpense[]>([]);

  private billOfMaterialsId: string | null = null;
  private referrerType: string | null = null;
  private referrerId: string | null = null;
  protected readonly printing = signal(false);
  protected routeJournalId = '';

  protected readonly isDraft = computed(() => {
    const doc = this.journal();
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
      !!this.warehouseId() &&
      this.outputQuantity() > 0 &&
      this.rawMaterials().length > 0 &&
      this.rawMaterials().every((l) => l.productId && l.quantity > 0) &&
      this.byProducts().every((l) => l.productId && l.quantity > 0) &&
      this.expenses().every((l) => l.costTermId && l.amount >= 0) &&
      this.allocationIsSane(),
  );

  protected readonly canApprove = computed(() => !this.isNew() && this.isDraft() && this.canSave());

  constructor() {
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({ next: (w) => this.warehouses.set(w) });
    this.accountingService.listAllAccounts(this.organizationId).subscribe({ next: (a) => this.accounts.set(a) });
    this.configurationService.listCostTerms(this.organizationId).subscribe({
      next: (terms) => this.costTerms.set(terms.filter((t) => t.category === 'ProductionCost' && t.isActive)),
    });

    this.route.paramMap.subscribe((params) => {
      this.routeJournalId = params.get('productionJournalId')!;
      const isNew = this.routeJournalId === 'new';
      this.isNew.set(isNew);
      this.journal.set(null);
      this.errorMessage.set(null);
      this.warningMessage.set(null);

      if (isNew) {
        this.resetForm();
        const fromOrderId = this.route.snapshot.queryParamMap.get('fromProductionOrder');
        if (fromOrderId) {
          this.loadConversionTemplate(fromOrderId);
        } else {
          this.loading.set(false);
        }
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

  protected onWarehouse(event: Event): void {
    this.warehouseId.set((event.target as HTMLSelectElement).value);
  }

  protected onNotes(event: Event): void {
    this.notes.set((event.target as HTMLTextAreaElement).value);
  }

  /** "LOAD BOM" -- an explicit, user-invoked template load, exactly as the reference product does
   * it. It fills editable defaults scaled to this run's output; nothing afterwards enforces them. */
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

  protected save(): void {
    if (!this.canSave()) return;

    this.saving.set(true);
    this.errorMessage.set(null);

    const request: ProductionJournalRequest = {
      date: this.date(),
      reference: this.reference().trim() || null,
      productId: this.productId(),
      outputQuantity: this.outputQuantity(),
      warehouseId: this.warehouseId(),
      billOfMaterialsId: this.billOfMaterialsId,
      notes: this.notes().trim() || null,
      referrerType: this.referrerType,
      referrerId: this.referrerId,
      rawMaterials: this.rawMaterials().map((l) => ({ productId: l.productId, quantity: l.quantity })),
      byProducts: this.byProducts().map((l) => ({
        productId: l.productId,
        costAllocationPct: l.costAllocationPct,
        quantity: l.quantity,
      })),
      expenses: this.expenses().map((l) => ({ costTermId: l.costTermId, amount: l.amount })),
    };

    if (this.isNew()) {
      this.manufacturingService.createProductionJournal(this.organizationId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), (r) => r.id, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: (result) => {
            this.saving.set(false);
            void this.router.navigate([
              '/organizations',
              this.organizationId,
              'manufacturing',
              'production-journals',
              result.id,
            ]);
          },
          error: (err: unknown) => this.fail(err, 'Could not create the production journal.'),
        });
    } else {
      this.manufacturingService.updateProductionJournal(this.organizationId, this.routeJournalId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), () => this.routeJournalId, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: () => {
            this.saving.set(false);
            this.load();
          },
          error: (err: unknown) => this.fail(err, 'Could not save the production journal.'),
        });
    }
  }

  /** A 422 is the confirmable stock warning, not a failure: re-approving with the override flag is
   * the documented way through, exactly as Invoice does it. */
  protected approve(overrideWarning = false): void {
    this.approving.set(true);
    this.errorMessage.set(null);
    this.warningMessage.set(null);

    this.manufacturingService.approveProductionJournal(this.organizationId, this.routeJournalId, overrideWarning)
      .subscribe({
        next: () => {
          this.approving.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.approving.set(false);
          const status = (err as { status?: number }).status;
          const message = extractErrorMessage(err) ?? 'Could not approve the production journal.';
          if (status === 422) {
            this.warningMessage.set(message);
          } else {
            this.errorMessage.set(message);
          }
        },
      });
  }

  protected voidDocument(): void {
    this.voiding.set(true);
    this.errorMessage.set(null);
    this.manufacturingService.voidProductionJournal(this.organizationId, this.routeJournalId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void the production journal.');
      },
    });
  }

  private fail(err: unknown, fallback: string): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }

  private resetForm(): void {
    this.date.set(this.today());
    this.reference.set('');
    this.productId.set('');
    this.outputQuantity.set(1);
    this.warehouseId.set('');
    this.notes.set('');
    this.rawMaterials.set([{ key: nextKey++, productId: '', quantity: 1 }]);
    this.byProducts.set([]);
    this.expenses.set([]);
    this.billOfMaterialsId = null;
    this.referrerType = null;
    this.referrerId = null;
  }

  private loadConversionTemplate(productionOrderId: string): void {
    this.loading.set(true);
    this.manufacturingService.getProductionJournalTemplate(this.organizationId, productionOrderId).subscribe({
      next: (template) => {
        this.date.set(template.date);
        this.reference.set(template.reference ?? '');
        this.productId.set(template.productId);
        this.outputQuantity.set(template.outputQuantity);
        this.notes.set(template.notes ?? '');
        this.billOfMaterialsId = template.billOfMaterialsId;
        this.referrerType = template.referrerType;
        this.referrerId = template.referrerId;
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
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the production order.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.manufacturingService.getProductionJournal(this.organizationId, this.routeJournalId).subscribe({
      next: (detail) => {
        this.journal.set(detail);
        this.date.set(detail.date);
        this.reference.set(detail.reference ?? '');
        this.productId.set(detail.productId);
        this.outputQuantity.set(detail.outputQuantity);
        this.warehouseId.set(detail.warehouseId);
        this.notes.set(detail.notes ?? '');
        this.billOfMaterialsId = detail.billOfMaterialsId;
        this.referrerType = detail.referrerType;
        this.referrerId = detail.referrerId;
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
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the production journal.');
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

    this.printingService.printDocument(this.organizationId, 'ProductionJournal', this.routeJournalId).subscribe({
      next: (blob) => {
        this.printing.set(false);
        openBlobInNewTab(blob, tab);
      },
      error: (err: unknown) => {
        this.printing.set(false);
        tab?.close();
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print production journal. Please try again.');
      },
    });
  }
}
