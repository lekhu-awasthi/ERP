import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { PrintingTemplate } from '../../../core/configuration/configuration.models';
import { DocumentType } from '../../../core/sales/sales.models';

/** The subset of DocumentType this screen offers -- excludes the numbering-pool-only types
 * (Account/Contact/Product) and the day-zero types (OpeningBalance/OpeningStock), neither of
 * which is a printable transaction document (see docs/phase-20d-status.md's scope decision). */
const PRINTABLE_DOCUMENT_TYPES: DocumentType[] = [
  'Quotation', 'SalesOrder', 'Invoice', 'CreditNote', 'PurchaseOrder', 'PurchaseBill', 'Expense', 'DebitNote', 'Payment',
  'JournalVoucher', 'CashTransfer', 'WarehouseTransfer', 'InventoryAdjustment', 'ProductionOrder', 'ProductionJournal',
];

/**
 * Roadmap Phase 20d: Admin can create/edit/rename Printing Templates and choose which one is the
 * default per DocumentType. Deliberately metadata-only -- no layout editor (see
 * PrintingTemplate's own backend doc comment for why the real product's visual template designer
 * was judged out of scope). One flat table grouped visually by DocumentType (rather than 15
 * separate sections, unlike CostTermListPage's 2-section split) since most tenants will only ever
 * populate a handful of the 15 offered document types.
 */
@Component({
  selector: 'app-printing-template-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './printing-template-list-page.html',
})
export class PrintingTemplateListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly configurationService = inject(ConfigurationService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly documentTypes = PRINTABLE_DOCUMENT_TYPES;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<PrintingTemplate[]>([]);
  protected readonly editingId = signal<string | null>(null);

  /** Sorted by DocumentType then Name so same-type rows sit together without a separate section
   * per type. */
  protected readonly sortedItems = computed(() =>
    [...this.items()].sort((a, b) => a.documentType.localeCompare(b.documentType) || a.name.localeCompare(b.name)),
  );

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    documentType: ['Invoice' as DocumentType, [Validators.required]],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', documentType: 'Invoice', isActive: true });
  }

  protected startEdit(item: PrintingTemplate): void {
    this.editingId.set(item.id);
    this.form.reset({ name: item.name, documentType: item.documentType, isActive: item.isActive });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, documentType, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    const request$ = editingId
      ? this.configurationService.updatePrintingTemplate(this.organizationId, editingId, { name, documentType, isActive })
      : this.configurationService.createPrintingTemplate(this.organizationId, { name, documentType });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save printing template. Please try again.');
      },
    });
  }

  protected setDefault(item: PrintingTemplate): void {
    this.configurationService.setDefaultPrintingTemplate(this.organizationId, item.id).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not set default printing template. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.configurationService.listPrintingTemplates(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load printing templates.');
      },
    });
  }
}
