import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import {
  CustomFieldDefinition, PrintElementsForType, PrintingTemplate, PrintingTemplateLocationChoice,
} from '../../../core/configuration/configuration.models';
import { DocumentType } from '../../../core/sales/sales.models';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { BillingLocationStore } from '../../../shared/locations/billing-location-store';
import { PrintingTemplateLayoutEditor } from './printing-template-layout-editor';

/** The subset of DocumentType this screen offers -- excludes the numbering-pool-only types
 * (Account/Contact/Product) and the day-zero types (OpeningBalance/OpeningStock), neither of
 * which is a printable transaction document (see docs/phase-20d-status.md's scope decision). */
const PRINTABLE_DOCUMENT_TYPES: DocumentType[] = [
  'Quotation', 'SalesOrder', 'Invoice', 'CreditNote', 'PurchaseOrder', 'PurchaseBill', 'Expense', 'DebitNote', 'Payment',
  'JournalVoucher', 'CashTransfer', 'WarehouseTransfer', 'InventoryAdjustment', 'ProductionOrder', 'ProductionJournal',
  // Phase 58 -- both listed on the live Printing Templates page.
  'DeliveryNote', 'GoodsReceivedNote',
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
  imports: [ReactiveFormsModule, RouterLink, StatusBanner, PrintingTemplateLayoutEditor],
  templateUrl: './printing-template-list-page.html',
})
export class PrintingTemplateListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly configurationService = inject(ConfigurationService);
  private readonly fb = inject(FormBuilder);
  private readonly locationStore = inject(BillingLocationStore);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly documentTypes = PRINTABLE_DOCUMENT_TYPES;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<PrintingTemplate[]>([]);
  protected readonly editingId = signal<string | null>(null);

  /** Phase 73 -- the template whose layout is open, as a one-item list so the editor is created afresh (and starts
   * from what is saved) each time another template is opened. */
  protected readonly layoutTemplateId = signal<string | null>(null);
  protected readonly layoutTemplate = computed(() => {
    const id = this.layoutTemplateId();
    const template = this.items().find((item) => item.id === id);
    return template ? [template] : [];
  });
  protected readonly elementTable = signal<PrintElementsForType[]>([]);
  protected readonly customFields = signal<CustomFieldDefinition[]>([]);

  /** Sorted by DocumentType then Name so same-type rows sit together without a separate section
   * per type. */
  protected readonly sortedItems = computed(() =>
    [...this.items()].sort((a, b) => a.documentType.localeCompare(b.documentType) || a.name.localeCompare(b.name)),
  );

  /**
   * Phase 75 -- each location may print a type with its own template instead of the default. Offered only to an
   * organization with more than one location, and only for the types that carry a location here (the server's own
   * list, `BillingLocationStore.applies`) and have an active template to choose; the server refuses the rest.
   */
  protected readonly locations = this.locationStore.locations(this.organizationId);
  protected readonly showByLocation = computed(() => this.locations().length > 1);
  protected readonly choices = signal<PrintingTemplateLocationChoice[]>([]);
  private readonly pickedLocationId = signal<string | null>(null);
  protected readonly selectedLocationId = computed(() => {
    const picked = this.pickedLocationId();
    const locations = this.locations();
    return locations.some((x) => x.id === picked) ? picked : (locations[0]?.id ?? null);
  });
  protected readonly selectedLocationName = computed(
    () => this.locations().find((x) => x.id === this.selectedLocationId())?.name ?? '',
  );
  protected readonly locationMessage = signal<string | null>(null);
  protected readonly locationRows = computed(() => {
    const locationId = this.selectedLocationId();
    const items = this.items();
    const choices = this.choices();
    return this.documentTypes
      .filter((type) => this.locationStore.applies(this.organizationId, type))
      .map((type) => {
        const templates = items.filter((x) => x.documentType === type && x.isActive)
          .sort((a, b) => a.name.localeCompare(b.name));
        const chosen = choices.find((x) => x.locationId === locationId && x.documentType === type);
        return {
          type,
          templates,
          chosenId: templates.some((x) => x.id === chosen?.printingTemplateId) ? chosen!.printingTemplateId : '',
          defaultName: items.find((x) => x.documentType === type && x.isDefault)?.name ?? null,
        };
      })
      .filter((row) => row.templates.length > 0);
  });

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    documentType: ['Invoice' as DocumentType, [Validators.required]],
    isActive: [true],
  });

  constructor() {
    this.load();
    this.loadChoices();
    this.configurationService.listPrintElements(this.organizationId).subscribe({
      next: (table) => this.elementTable.set(table),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the printable elements.'),
    });
    this.configurationService.listCustomFieldDefinitions(this.organizationId).subscribe({
      next: (fields) => this.customFields.set(fields),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load custom fields.'),
    });
  }

  protected openLayout(item: PrintingTemplate): void {
    this.layoutTemplateId.set(item.id);
  }

  /** Focus returns to the row's Layout button, so a keyboard user is not dropped onto the page (phase 72). */
  protected closeLayout(): void {
    const id = this.layoutTemplateId();
    this.layoutTemplateId.set(null);
    if (id) {
      setTimeout(() => document.getElementById(`printing-template-layout-${id}`)?.focus());
    }
  }

  protected reload(): void {
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
        this.loadChoices();
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

  protected pickLocation(event: Event): void {
    this.pickedLocationId.set((event.target as HTMLSelectElement).value);
    this.locationMessage.set(null);
  }

  /** An empty value is "the organization's default", which clears the location's own choice. */
  protected chooseForLocation(type: DocumentType, previousId: string, event: Event): void {
    const locationId = this.selectedLocationId();
    if (!locationId) {
      return;
    }

    const select = event.target as HTMLSelectElement;
    const templateId = select.value;
    const name = this.items().find((x) => x.id === templateId)?.name;
    const location = this.selectedLocationName();
    this.errorMessage.set(null);
    this.locationMessage.set(null);

    if (templateId) {
      this.configurationService.setPrintingTemplateLocationChoice(this.organizationId, locationId, templateId).subscribe({
        next: () => this.afterChoice(`${location} now prints ${type} with ${name}.`),
        error: (err: unknown) => this.failChoice(err, select, previousId),
      });
    } else {
      this.configurationService.clearPrintingTemplateLocationChoice(this.organizationId, locationId, type).subscribe({
        next: () => this.afterChoice(`${location} now prints ${type} with the organization's default.`),
        error: (err: unknown) => this.failChoice(err, select, previousId),
      });
    }
  }

  private afterChoice(message: string): void {
    this.locationMessage.set(message);
    this.loadChoices();
  }

  /** The select already shows the refused value and its `[selected]` bindings did not change, so Angular will not
   * move it back; it is put back by hand (phase 64's cancelled-change rule). */
  private failChoice(err: unknown, select: HTMLSelectElement, previousId: string): void {
    this.errorMessage.set(extractErrorMessage(err) ?? "Could not change the location's template. Please try again.");
    select.value = previousId;
  }

  private loadChoices(): void {
    this.configurationService.listPrintingTemplateLocationChoices(this.organizationId).subscribe({
      next: (choices) => this.choices.set(choices),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? "Could not load the locations' templates."),
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
