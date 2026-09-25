import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage, extractWarningKind } from '../../../core/auth/api-error';
import { BASE_CURRENCY_CODE, Warehouse } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { SalesService } from '../../../core/sales/sales.service';
import {
  DeliveryNoteDetail,
  DeliveryNoteLineInput,
  DeliveryNoteRequest,
  DocumentType,
} from '../../../core/sales/sales.models';
import { PendingTemplateStore } from '../../../core/sales/pending-template.store';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, VatRate } from '../../../core/catalog/catalog.models';
import { PrintingService } from '../../../core/printing/printing.service';
import { openBlankTabForPrint, openBlobInNewTab } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { CurrencyRateFields } from '../../../shared/currency/currency-rate-fields';
import { DocumentTabs } from '../../../shared/document-tabs/document-tabs';
import { ReportingTagsEditor } from '../../../shared/reporting-tags/reporting-tags-editor';
import { CustomFieldsEditor } from '../../../shared/custom-fields/custom-fields-editor';
import { commitCustomFieldsThen } from '../../../shared/custom-fields/commit-custom-fields';
import { TermsEditor } from '../../../shared/terms/terms-editor';
import { SendEmailDialog } from '../../../shared/send-email/send-email-dialog';
import { DocumentLocationPicker } from '../../../shared/locations/document-location-picker';
import { defaultWarehouseSeed } from '../../../shared/locations/default-warehouse-seed';
import { locationAwareProducts } from '../../../shared/catalog/location-aware-products';
import { FieldError, FieldErrorMessage } from '../../../shared/a11y/field-error';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { LineUnitControl, LineUnitOption } from '../../../shared/catalog/line-unit-control';
import { UnitOfMeasurementStore } from '../../../shared/catalog/unit-of-measurement-store';

interface EditableLine {
  key: number;
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  discountPct: number;
  /** Phase 52 -- the unit the line is entered in; empty means the product's primary unit. */
  unitId: string;
}

let nextLineKey = 1;

/**
 * Phase 58 -- the Delivery Note, create and edit on one component.
 *
 * <p>The Sales Order form plus what the live Delivery Note form adds: a Warehouse (required -- the
 * goods leave from somewhere), an Expected Delivery Date (required, defaulting to the day after), a
 * Shipping Address and a Tracking No; Terms and Conditions as the Sales Order carries them.</p>
 *
 * <p>Approving issues the Goods lines from the <b>physical</b> ledger, through the tenant's Negative
 * Item Balance setting -- the same Reject / Warn / Do Nothing the Invoice gets from the accounting
 * ledger, so Approve has the Invoice's confirm-and-retry flow on a 422.</p>
 */
@Component({
  selector: 'app-delivery-note-detail-page',
  imports: [
    RouterLink, AmountPipe, BsDateInput, DocumentTabs, ReportingTagsEditor, CustomFieldsEditor, TermsEditor,
    CurrencyRateFields, SendEmailDialog, DocumentLocationPicker, StatusBanner, FieldErrorMessage, LineUnitControl,
  ],
  templateUrl: './delivery-note-detail-page.html',
})
export class DeliveryNoteDetailPage {
  private readonly customFieldsEditor = viewChild(CustomFieldsEditor);

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly salesService = inject(SalesService);
  private readonly contactsService = inject(ContactsService);
  private readonly catalogService = inject(CatalogService);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly printingService = inject(PrintingService);
  private readonly pendingTemplateStore = inject(PendingTemplateStore);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly units = inject(UnitOfMeasurementStore).units(this.organizationId);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly printing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly fieldError = new FieldError(this.errorMessage);

  protected readonly note = signal<DeliveryNoteDetail | null>(null);
  protected readonly customers = signal<Contact[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly warehouseId = signal('');
  protected readonly date = signal(this.today());
  protected readonly expectedDeliveryDate = signal(this.tomorrow());
  protected readonly reference = signal('');
  protected readonly trackingNo = signal('');
  protected readonly shippingAddress = signal('');
  protected readonly locationId = signal('');
  protected readonly currencyCode = signal(BASE_CURRENCY_CODE);
  protected readonly exchangeRate = signal(1);
  protected readonly terms = signal('');
  protected readonly lines = signal<EditableLine[]>([]);
  protected readonly discountPct = signal(0);

  /** The Sales Order a converted note delivers, fixed at create. */
  private readonly referrerType = signal<DocumentType | null>(null);
  private readonly referrerId = signal<string | null>(null);

  protected readonly vatRates: VatRate[] = ['NoVat', 'ZeroVat', 'ThirteenPercentVat'];
  protected routeNoteId = '';

  protected readonly warehouseSeed = defaultWarehouseSeed(this.warehouseId, this.warehouses);

  protected readonly subTotal = computed(() =>
    this.round(this.lines().reduce((sum, l) => sum + this.netAfterLineDiscount(l), 0)),
  );
  protected readonly discountAmount = computed(() => this.round((this.subTotal() * this.discountPct()) / 100));
  protected readonly nonTaxableTotal = computed(() =>
    this.round(this.lines().filter((l) => this.vatPercent(l.vatRate) === 0).reduce((sum, l) => sum + this.netAfterBothDiscounts(l), 0)),
  );
  protected readonly taxableTotal = computed(() =>
    this.round(this.lines().filter((l) => this.vatPercent(l.vatRate) > 0).reduce((sum, l) => sum + this.netAfterBothDiscounts(l), 0)),
  );
  protected readonly vatTotal = computed(() =>
    this.round(this.lines().reduce((sum, l) => sum + this.netAfterBothDiscounts(l) * this.vatPercent(l.vatRate), 0)),
  );
  protected readonly grandTotal = computed(() => this.round(this.taxableTotal() + this.nonTaxableTotal() + this.vatTotal()));

  protected readonly isDraft = computed(() => {
    const note = this.note();
    return this.isNew() || !note || note.status === 'Draft';
  });

  /** A note raised from a Sales Order stays that order's customer's. */
  protected readonly customerLocked = computed(() => !this.isDraft() || this.referrerId() !== null || !!this.note()?.referrerId);

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew() && lines.length >= 1 && lines.every((l) => l.productId && l.quantity > 0);
  });

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Customer').subscribe({ next: (c) => this.customers.set(c) });
    locationAwareProducts(this.organizationId, this.locationId, this.products);
    this.organizationsService.listWarehouses(this.organizationId).subscribe({
      next: (w) => {
        this.warehouses.set(w);
        this.warehouseSeed.retry();
      },
    });

    this.route.paramMap.subscribe((params) => {
      this.routeNoteId = params.get('deliveryNoteId')!;
      const isNew = this.routeNoteId === 'new';
      this.isNew.set(isNew);
      this.note.set(null);
      this.errorMessage.set(null);
      this.referrerType.set(null);
      this.referrerId.set(null);

      if (isNew) {
        this.loading.set(false);
        this.resetForm();

        // Convert to Delivery Note from a Sales Order hands its prefill over here, once.
        const template = this.pendingTemplateStore.takeDeliveryNoteTemplate();
        if (template) {
          this.referrerType.set(template.referrerType);
          this.referrerId.set(template.referrerId);
          this.contactId.set(template.contactId);
          this.date.set(template.date);
          this.expectedDeliveryDate.set(template.expectedDeliveryDate);
          this.reference.set(template.reference ?? '');
          this.discountPct.set(template.discountPct);
          this.terms.set(template.terms ?? '');
          this.currencyCode.set(template.currencyCode);
          this.exchangeRate.set(template.exchangeRate);
          this.locationId.set(template.locationId ?? '');
          this.lines.set(
            template.lines.map((l) => ({
              key: nextLineKey++,
              productId: l.productId,
              quantity: l.quantity,
              rate: l.rate,
              vatRate: l.vatRate,
              discountPct: l.discountPct,
              unitId: l.unitId ?? '',
            })),
          );
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

  /** The Sales Order's rate suggestion (phase 31): the tenant's own Suggest Selling Price setting. */
  protected onProductChange(key: number, event: Event): void {
    const productId = (event.target as HTMLSelectElement).value;
    const product = this.products().find((p) => p.id === productId);
    this.updateLine(key, { productId, unitId: '', rate: product?.sellingPrice ?? 0, vatRate: product?.vatRate ?? 'NoVat' });

    if (!productId) {
      return;
    }

    this.catalogService.suggestProductRate(this.organizationId, productId).subscribe({
      next: (suggestion) => this.updateLine(key, { rate: suggestion.rate, vatRate: suggestion.vatRate }),
      error: () => undefined,
    });
  }

  protected onQuantityChange(key: number, event: Event): void {
    const quantity = (event.target as HTMLInputElement).valueAsNumber;
    this.updateLine(key, { quantity: Number.isFinite(quantity) ? quantity : 0 });
  }

  protected onRateChange(key: number, event: Event): void {
    const rate = (event.target as HTMLInputElement).valueAsNumber;
    this.updateLine(key, { rate: Number.isFinite(rate) ? rate : 0 });
  }

  protected onVatRateChange(key: number, event: Event): void {
    this.updateLine(key, { vatRate: (event.target as HTMLSelectElement).value as VatRate });
  }

  protected onDiscountPctChange(key: number, event: Event): void {
    const discountPct = (event.target as HTMLInputElement).valueAsNumber;
    this.updateLine(key, { discountPct: Number.isFinite(discountPct) ? discountPct : 0 });
  }

  protected onHeaderDiscountPctChange(event: Event): void {
    const discountPct = (event.target as HTMLInputElement).valueAsNumber;
    this.discountPct.set(Number.isFinite(discountPct) ? discountPct : 0);
  }

  /** Phase 52 -- a unit sets the Rate from that unit row's selling price, and nothing else. */
  protected onUnitChange(key: number, option: LineUnitOption): void {
    this.updateLine(key, { unitId: option.unitId, rate: option.sellingPrice });
  }

  protected unitLabel(line: EditableLine): string {
    const product = this.products().find((p) => p.id === line.productId);
    const unitId = line.unitId || product?.primaryUnitId;
    return this.units().find((u) => u.id === unitId)?.shortName ?? '';
  }

  protected addLine(): void {
    this.lines.update((lines) => [...lines, this.newLine()]);
  }

  protected removeLine(key: number): void {
    this.lines.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected saveDraft(): void {
    if (!this.contactId()) {
      this.fieldError.fail('delivery-note-detail-page-customer', 'Select a Customer.');
      return;
    }

    if (!this.warehouseId()) {
      this.fieldError.fail('delivery-note-detail-page-warehouse', 'Select the Warehouse the goods leave from.');
      return;
    }

    if (!this.expectedDeliveryDate()) {
      // The banner, not a field error: `app-bs-date-input` owns its own input and exposes none of
      // the three bindings a field error needs (a11y-sweep-guard, phase 47).
      this.errorMessage.set('Enter the Expected Delivery Date.');
      return;
    }

    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const request: DeliveryNoteRequest = {
      contactId: this.contactId(),
      warehouseId: this.warehouseId(),
      date: this.date(),
      expectedDeliveryDate: this.expectedDeliveryDate(),
      reference: this.reference() || null,
      trackingNo: this.trackingNo() || null,
      shippingAddress: this.shippingAddress() || null,
      lines,
      discountPct: this.discountPct(),
      terms: this.terms() || null,
      referrerType: this.referrerType(),
      referrerId: this.referrerId(),
      currencyCode: this.currencyCode(),
      exchangeRate: this.exchangeRate(),
      locationId: this.locationId() || null,
    };

    if (this.isNew()) {
      this.salesService.createDeliveryNote(this.organizationId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), (r) => r.id, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: (result) => {
            this.saving.set(false);
            this.router.navigate(['/organizations', this.organizationId, 'sales', 'delivery-notes', result.id]);
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save delivery note. Please try again.');
          },
        });
    } else {
      this.salesService.updateDeliveryNote(this.organizationId, this.routeNoteId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), () => this.routeNoteId, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: () => {
            this.saving.set(false);
            this.load();
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save delivery note. Please try again.');
          },
        });
    }
  }

  /**
   * The Invoice's Warn-and-continue flow: a 422 naming StockAvailability is the tenant's Warn
   * verdict on the physical ledger, confirmable; anything else is a real error.
   */
  protected approve(overrideWarning = false): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.salesService.approveDeliveryNote(this.organizationId, this.routeNoteId, overrideWarning).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);

        if (extractWarningKind(err) === 'StockAvailability') {
          const message = extractErrorMessage(err) ?? 'This delivery note exceeds the stock in the warehouse.';
          if (window.confirm(`${message}\n\nApprove anyway?`)) {
            this.approve(true);
            return;
          }
          this.errorMessage.set(message);
          return;
        }

        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve delivery note. Please try again.');
      },
    });
  }

  protected print(): void {
    this.printing.set(true);
    this.errorMessage.set(null);
    const tab = openBlankTabForPrint();

    this.printingService.printDocument(this.organizationId, 'DeliveryNote', this.routeNoteId).subscribe({
      next: (blob) => {
        this.printing.set(false);
        openBlobInNewTab(blob, tab);
      },
      error: (err: unknown) => {
        this.printing.set(false);
        tab?.close();
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print delivery note. Please try again.');
      },
    });
  }

  protected voidNote(): void {
    if (!window.confirm('Void this delivery note? The goods return to the physical stock. This cannot be undone.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.salesService.voidDeliveryNote(this.organizationId, this.routeNoteId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void delivery note. Please try again.');
      },
    });
  }

  protected vatPercent(vatRate: VatRate): number {
    return vatRate === 'ThirteenPercentVat' ? 0.13 : 0;
  }

  private netAfterLineDiscount(line: EditableLine): number {
    return line.quantity * line.rate * (1 - line.discountPct / 100);
  }

  private netAfterBothDiscounts(line: EditableLine): number {
    return this.netAfterLineDiscount(line) * (1 - this.discountPct() / 100);
  }

  private toLineInputs(): DeliveryNoteLineInput[] | null {
    const lines = this.lines()
      .filter((l) => l.productId && l.quantity > 0)
      .map((l) => ({
        productId: l.productId,
        quantity: l.quantity,
        rate: l.rate,
        vatRate: l.vatRate,
        discountPct: l.discountPct,
        unitId: l.unitId || null,
      }));

    if (lines.length === 0) {
      this.fieldError.fail('delivery-note-detail-page-add-line', 'Add at least one line with a Product and a Quantity.');
      return null;
    }

    return lines;
  }

  private updateLine(key: number, patch: Partial<Omit<EditableLine, 'key'>>): void {
    this.lines.update((lines) => lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  }

  private newLine(): EditableLine {
    return { key: nextLineKey++, productId: '', quantity: 1, rate: 0, vatRate: 'NoVat', discountPct: 0, unitId: '' };
  }

  private resetForm(): void {
    this.contactId.set('');
    this.warehouseId.set('');
    this.date.set(this.today());
    this.expectedDeliveryDate.set(this.tomorrow());
    this.reference.set('');
    this.trackingNo.set('');
    this.shippingAddress.set('');
    this.locationId.set('');
    this.currencyCode.set(BASE_CURRENCY_CODE);
    this.exchangeRate.set(1);
    this.terms.set('');
    this.discountPct.set(0);
    this.lines.set([this.newLine()]);
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  /** The live form's default Expected Delivery Date: the day after the note's own date. */
  private tomorrow(): string {
    const next = new Date();
    next.setDate(next.getDate() + 1);
    return next.toISOString().slice(0, 10);
  }

  private round(value: number): number {
    return Math.round(value * 100) / 100;
  }

  private load(): void {
    this.loading.set(true);
    this.salesService.getDeliveryNote(this.organizationId, this.routeNoteId).subscribe({
      next: (note) => {
        this.note.set(note);
        this.contactId.set(note.contactId);
        this.warehouseId.set(note.warehouseId);
        this.date.set(note.date);
        this.expectedDeliveryDate.set(note.expectedDeliveryDate);
        this.reference.set(note.reference ?? '');
        this.trackingNo.set(note.trackingNo ?? '');
        this.shippingAddress.set(note.shippingAddress ?? '');
        this.locationId.set(note.locationId ?? '');
        this.currencyCode.set(note.currencyCode);
        this.exchangeRate.set(note.exchangeRate);
        this.terms.set(note.terms ?? '');
        this.discountPct.set(note.discountPct);
        this.lines.set(
          note.lines.length > 0
            ? note.lines.map((l) => ({
                key: nextLineKey++,
                productId: l.productId,
                quantity: l.quantity,
                rate: l.rate,
                vatRate: l.vatRate,
                discountPct: l.discountPct,
                unitId: l.unitId ?? '',
              }))
            : [this.newLine()],
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load delivery note.');
      },
    });
  }
}
