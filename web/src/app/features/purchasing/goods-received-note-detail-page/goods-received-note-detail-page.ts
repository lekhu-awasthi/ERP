import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { BASE_CURRENCY_CODE, Warehouse } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import {
  GoodsReceivedNoteDetail,
  GoodsReceivedNoteLineInput,
  GoodsReceivedNoteRequest,
} from '../../../core/purchasing/purchasing.models';
import { PendingTemplateStore } from '../../../core/sales/pending-template.store';
import { DocumentType } from '../../../core/sales/sales.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
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
 * Phase 58 -- the Goods Received Note, create and edit on one component (phase 3's routing rule).
 *
 * <p>The Purchase Order form plus the three fields the live GRN form adds: a Warehouse (required --
 * the goods land somewhere), a Tracking No, and the Purchase Order it was received against, shown as
 * a link once saved. No Terms and Conditions block: the live form has none, which is phase 27b's own
 * dividing line (a GRN records something received).</p>
 *
 * <p>Approving receives the Goods lines into the <b>physical</b> ledger only. The money on the lines
 * is what the document says; nothing is posted, and the accounting stock does not move until the
 * Purchase Bill.</p>
 */
@Component({
  selector: 'app-goods-received-note-detail-page',
  imports: [
    RouterLink, AmountPipe, BsDateInput, DocumentTabs, ReportingTagsEditor, CustomFieldsEditor,
    CurrencyRateFields, SendEmailDialog, DocumentLocationPicker, StatusBanner, FieldErrorMessage, LineUnitControl,
  ],
  templateUrl: './goods-received-note-detail-page.html',
})
export class GoodsReceivedNoteDetailPage {
  private readonly customFieldsEditor = viewChild(CustomFieldsEditor);

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly purchasingService = inject(PurchasingService);
  private readonly contactsService = inject(ContactsService);
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

  protected readonly note = signal<GoodsReceivedNoteDetail | null>(null);
  protected readonly suppliers = signal<Contact[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly warehouseId = signal('');
  protected readonly date = signal(this.today());
  protected readonly reference = signal('');
  protected readonly trackingNo = signal('');
  protected readonly locationId = signal('');
  protected readonly currencyCode = signal(BASE_CURRENCY_CODE);
  protected readonly exchangeRate = signal(1);
  protected readonly lines = signal<EditableLine[]>([]);
  protected readonly discountPct = signal(0);

  /** The Purchase Order a converted GRN was received against. Carried into the create request
   * and fixed thereafter -- the server refuses to move a GRN off its order's supplier. */
  private readonly referrerType = signal<DocumentType | null>(null);
  private readonly referrerId = signal<string | null>(null);

  protected readonly vatRates: VatRate[] = ['NoVat', 'ZeroVat', 'ThirteenPercentVat'];
  protected routeNoteId = '';

  /** Phase 35a (Decision D) -- the billing location's default warehouse, as a one-shot prefill. */
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

  /** A GRN raised from a Purchase Order stays that order's supplier's; the picker says so. */
  protected readonly supplierLocked = computed(() => !this.isDraft() || this.referrerId() !== null || !!this.note()?.referrerId);

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew() && lines.length >= 1 && lines.every((l) => l.productId && l.quantity > 0);
  });

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Supplier').subscribe({ next: (c) => this.suppliers.set(c) });
    locationAwareProducts(this.organizationId, this.locationId, this.products);
    this.organizationsService.listWarehouses(this.organizationId).subscribe({
      next: (w) => {
        this.warehouses.set(w);
        this.warehouseSeed.retry();
      },
    });

    this.route.paramMap.subscribe((params) => {
      this.routeNoteId = params.get('goodsReceivedNoteId')!;
      const isNew = this.routeNoteId === 'new';
      this.isNew.set(isNew);
      this.note.set(null);
      this.errorMessage.set(null);
      this.referrerType.set(null);
      this.referrerId.set(null);

      if (isNew) {
        this.loading.set(false);
        this.resetForm();

        // Convert to Goods Received Note from a Purchase Order hands its prefill over here, once.
        const template = this.pendingTemplateStore.takeGoodsReceivedNoteTemplate();
        if (template) {
          this.referrerType.set(template.referrerType);
          this.referrerId.set(template.referrerId);
          this.contactId.set(template.contactId);
          this.date.set(template.date);
          this.reference.set(template.reference ?? '');
          this.discountPct.set(template.discountPct);
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

  protected onProductChange(key: number, event: Event): void {
    const productId = (event.target as HTMLSelectElement).value;
    const product = this.products().find((p) => p.id === productId);
    this.updateLine(key, { productId, unitId: '', rate: product?.purchasePrice ?? 0, vatRate: product?.vatRate ?? 'NoVat' });
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

  /** Phase 52 -- a unit sets the Rate from that unit row's purchase price, and nothing else. */
  protected onUnitChange(key: number, option: LineUnitOption): void {
    this.updateLine(key, { unitId: option.unitId, rate: option.purchasePrice });
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
      this.fieldError.fail('goods-received-note-detail-page-supplier', 'Select a Supplier.');
      return;
    }

    if (!this.warehouseId()) {
      this.fieldError.fail('goods-received-note-detail-page-warehouse', 'Select the Warehouse the goods were received into.');
      return;
    }

    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const request: GoodsReceivedNoteRequest = {
      contactId: this.contactId(),
      warehouseId: this.warehouseId(),
      date: this.date(),
      reference: this.reference() || null,
      trackingNo: this.trackingNo() || null,
      lines,
      discountPct: this.discountPct(),
      referrerType: this.referrerType(),
      referrerId: this.referrerId(),
      currencyCode: this.currencyCode(),
      exchangeRate: this.exchangeRate(),
      locationId: this.locationId() || null,
    };

    if (this.isNew()) {
      this.purchasingService.createGoodsReceivedNote(this.organizationId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), (r) => r.id, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: (result) => {
            this.saving.set(false);
            this.router.navigate(['/organizations', this.organizationId, 'purchasing', 'goods-received-notes', result.id]);
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save goods received note. Please try again.');
          },
        });
    } else {
      this.purchasingService.updateGoodsReceivedNote(this.organizationId, this.routeNoteId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), () => this.routeNoteId, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: () => {
            this.saving.set(false);
            this.load();
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save goods received note. Please try again.');
          },
        });
    }
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.purchasingService.approveGoodsReceivedNote(this.organizationId, this.routeNoteId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve goods received note. Please try again.');
      },
    });
  }

  protected print(): void {
    this.printing.set(true);
    this.errorMessage.set(null);
    const tab = openBlankTabForPrint();

    this.printingService.printDocument(this.organizationId, 'GoodsReceivedNote', this.routeNoteId).subscribe({
      next: (blob) => {
        this.printing.set(false);
        openBlobInNewTab(blob, tab);
      },
      error: (err: unknown) => {
        this.printing.set(false);
        tab?.close();
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print goods received note. Please try again.');
      },
    });
  }

  protected voidNote(): void {
    if (!window.confirm('Void this goods received note? The goods it received leave the physical stock. This cannot be undone.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.purchasingService.voidGoodsReceivedNote(this.organizationId, this.routeNoteId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void goods received note. Please try again.');
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

  private toLineInputs(): GoodsReceivedNoteLineInput[] | null {
    const lines = this.lines()
      .filter((l) => l.productId && l.quantity > 0)
      // Phase 52 -- null, not '', for the primary unit: the field is optional on the request, so a
      // form that forgot it would compile and save a carton line as pieces.
      .map((l) => ({
        productId: l.productId,
        quantity: l.quantity,
        rate: l.rate,
        vatRate: l.vatRate,
        discountPct: l.discountPct,
        unitId: l.unitId || null,
      }));

    if (lines.length === 0) {
      this.fieldError.fail('goods-received-note-detail-page-add-line', 'Add at least one line with a Product and a Quantity.');
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
    this.reference.set('');
    this.trackingNo.set('');
    this.locationId.set('');
    this.currencyCode.set(BASE_CURRENCY_CODE);
    this.exchangeRate.set(1);
    this.discountPct.set(0);
    this.lines.set([this.newLine()]);
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private round(value: number): number {
    return Math.round(value * 100) / 100;
  }

  private load(): void {
    this.loading.set(true);
    this.purchasingService.getGoodsReceivedNote(this.organizationId, this.routeNoteId).subscribe({
      next: (note) => {
        this.note.set(note);
        this.contactId.set(note.contactId);
        this.warehouseId.set(note.warehouseId);
        this.date.set(note.date);
        this.reference.set(note.reference ?? '');
        this.trackingNo.set(note.trackingNo ?? '');
        this.locationId.set(note.locationId ?? '');
        this.currencyCode.set(note.currencyCode);
        this.exchangeRate.set(note.exchangeRate);
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
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load goods received note.');
      },
    });
  }
}
