import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { ExpenditureClassification, PurchaseBillDetail, PurchaseBillLineInput } from '../../../core/purchasing/purchasing.models';
import { DocumentType } from '../../../core/sales/sales.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, VatRate } from '../../../core/catalog/catalog.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { TdsType } from '../../../core/configuration/configuration.models';
import { PendingTemplateStore } from '../../../core/sales/pending-template.store';

interface EditableLine {
  key: number;
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  expenditureClassification: ExpenditureClassification;
}

let nextLineKey = 1;

/** Clones invoice-detail-page's chrome (Warehouse required, GL-preview-free -- PurchaseBill has no
 * live preview endpoint parity yet, see preview button below) plus Purchase-specific fields
 * confirmed live: Supplier Invoice Reference, Is Import + Import Details, TDS Type (TdsAmount is
 * server-computed, shown read-only once loaded). "Convert to Debit Note" mirrors Invoice's
 * "Convert to Credit Note". */
@Component({
  selector: 'app-purchase-bill-detail-page',
  imports: [RouterLink, DatePipe],
  templateUrl: './purchase-bill-detail-page.html',
})
export class PurchaseBillDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly purchasingService = inject(PurchasingService);
  private readonly contactsService = inject(ContactsService);
  private readonly catalogService = inject(CatalogService);
  private readonly accountingService = inject(AccountingService);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly configurationService = inject(ConfigurationService);
  private readonly pendingTemplateStore = inject(PendingTemplateStore);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly converting = signal(false);
  protected readonly previewingGl = signal(false);
  protected readonly glPreview = signal<{ accountId: string; debit: number; credit: number }[] | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly purchaseBill = signal<PurchaseBillDetail | null>(null);
  protected readonly suppliers = signal<Contact[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly tdsTypes = signal<TdsType[]>([]);
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly warehouseId = signal('');
  protected readonly date = signal(this.today());
  protected readonly reference = signal('');
  protected readonly supplierInvoiceReference = signal('');
  protected readonly isImport = signal(false);
  protected readonly importCountry = signal('');
  protected readonly importDate = signal('');
  protected readonly importDocumentNo = signal('');
  protected readonly tdsTypeId = signal('');
  protected readonly lines = signal<EditableLine[]>([]);
  private referrerType: DocumentType | null = null;
  private referrerId: string | null = null;

  protected readonly vatRates: VatRate[] = ['NoVat', 'ZeroVat', 'ThirteenPercentVat'];
  protected readonly expenditureClassifications: ExpenditureClassification[] = ['Others', 'Capital'];

  private routePurchaseBillId = '';

  protected readonly lineTotal = computed(() => this.round(this.lines().reduce((sum, l) => sum + l.quantity * l.rate, 0)));
  protected readonly vatTotal = computed(() =>
    this.round(this.lines().reduce((sum, l) => sum + l.quantity * l.rate * this.vatPercent(l.vatRate), 0)),
  );
  protected readonly grandTotal = computed(() => this.round(this.lineTotal() + this.vatTotal()));

  protected readonly isDraft = computed(() => {
    const bill = this.purchaseBill();
    return this.isNew() || !bill || bill.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew() && lines.length >= 1 && lines.every((l) => l.productId && l.quantity > 0) && !!this.warehouseId();
  });

  constructor() {
    this.contactsService.listContacts(this.organizationId, 'Supplier').subscribe({ next: (c) => this.suppliers.set(c) });
    this.catalogService.listProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.accountingService.listAccounts(this.organizationId).subscribe({ next: (a) => this.accounts.set(a) });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({ next: (w) => this.warehouses.set(w) });
    this.configurationService.listTdsTypes(this.organizationId).subscribe({ next: (t) => this.tdsTypes.set(t) });

    this.route.paramMap.subscribe((params) => {
      this.routePurchaseBillId = params.get('purchaseBillId')!;
      const isNew = this.routePurchaseBillId === 'new';
      this.isNew.set(isNew);
      this.purchaseBill.set(null);
      this.errorMessage.set(null);
      this.referrerType = null;
      this.referrerId = null;

      if (isNew) {
        this.loading.set(false);
        const template = this.pendingTemplateStore.takePurchaseBillTemplate();
        if (template) {
          this.contactId.set(template.contactId);
          this.date.set(template.date);
          this.reference.set(template.reference ?? '');
          this.referrerType = template.referrerType;
          this.referrerId = template.referrerId;
          this.lines.set(
            template.lines.length > 0
              ? template.lines.map((l) => ({ key: nextLineKey++, ...l }))
              : [this.newLine()],
          );
        } else {
          this.contactId.set('');
          this.date.set(this.today());
          this.reference.set('');
          this.lines.set([this.newLine()]);
        }
        this.warehouseId.set('');
        this.supplierInvoiceReference.set('');
        this.isImport.set(false);
        this.importCountry.set('');
        this.importDate.set('');
        this.importDocumentNo.set('');
        this.tdsTypeId.set('');
      } else {
        this.load();
      }
    });
  }

  protected contactLabel(contactId: string): string {
    const contact = this.suppliers().find((c) => c.id === contactId);
    return contact ? `${contact.code} — ${contact.name}` : '—';
  }

  protected productLabel(productId: string): string {
    const product = this.products().find((p) => p.id === productId);
    return product ? `${product.code} — ${product.name}` : '—';
  }

  protected accountLabel(accountId: string): string {
    const account = this.accounts().find((a) => a.id === accountId);
    return account ? `${account.code} — ${account.name}` : '—';
  }

  protected tdsTypeLabel(tdsTypeId: string | null): string {
    const tdsType = this.tdsTypes().find((t) => t.id === tdsTypeId);
    return tdsType ? `${tdsType.code} — ${tdsType.name} (${tdsType.ratePct}%)` : '—';
  }

  protected onProductChange(key: number, event: Event): void {
    const productId = (event.target as HTMLSelectElement).value;
    const product = this.products().find((p) => p.id === productId);
    this.updateLine(key, { productId, rate: product?.purchasePrice ?? 0, vatRate: product?.vatRate ?? 'NoVat' });
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
    const vatRate = (event.target as HTMLSelectElement).value as VatRate;
    this.updateLine(key, { vatRate });
  }

  protected onExpenditureClassificationChange(key: number, event: Event): void {
    const expenditureClassification = (event.target as HTMLSelectElement).value as ExpenditureClassification;
    this.updateLine(key, { expenditureClassification });
  }

  protected addLine(): void {
    this.lines.update((lines) => [...lines, this.newLine()]);
  }

  protected removeLine(key: number): void {
    this.lines.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected previewGlPosting(): void {
    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    this.previewingGl.set(true);
    this.errorMessage.set(null);

    this.purchasingService.previewPurchaseBillGlPosting(this.organizationId, lines, this.tdsTypeId() || null).subscribe({
      next: (result) => {
        this.previewingGl.set(false);
        this.glPreview.set(result);
      },
      error: (err: unknown) => {
        this.previewingGl.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not preview GL posting.');
      },
    });
  }

  protected saveDraft(): void {
    if (!this.contactId()) {
      this.errorMessage.set('Select a Supplier.');
      return;
    }
    if (!this.warehouseId()) {
      this.errorMessage.set('Select a Warehouse.');
      return;
    }
    if (this.isImport() && (!this.importCountry() || !this.importDate() || !this.importDocumentNo())) {
      this.errorMessage.set('Import Country, Import Date and Import Document No are required when Is Import is set.');
      return;
    }

    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const request = {
      contactId: this.contactId(),
      warehouseId: this.warehouseId(),
      date: this.date(),
      reference: this.reference() || null,
      supplierInvoiceReference: this.supplierInvoiceReference() || null,
      isImport: this.isImport(),
      importCountry: this.isImport() ? this.importCountry() || null : null,
      importDate: this.isImport() ? this.importDate() || null : null,
      importDocumentNo: this.isImport() ? this.importDocumentNo() || null : null,
      tdsTypeId: this.tdsTypeId() || null,
      referrerType: this.referrerType,
      referrerId: this.referrerId,
      lines,
    };

    if (this.isNew()) {
      this.purchasingService.createPurchaseBill(this.organizationId, request).subscribe({
        next: (result) => {
          this.saving.set(false);
          this.router.navigate(['/organizations', this.organizationId, 'purchasing', 'purchase-bills', result.id]);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save purchase bill. Please try again.');
        },
      });
    } else {
      this.purchasingService.updatePurchaseBill(this.organizationId, this.routePurchaseBillId, request).subscribe({
        next: () => {
          this.saving.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save purchase bill. Please try again.');
        },
      });
    }
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.purchasingService.approvePurchaseBill(this.organizationId, this.routePurchaseBillId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve purchase bill. Please try again.');
      },
    });
  }

  protected convertToDebitNote(): void {
    this.converting.set(true);
    this.errorMessage.set(null);

    this.purchasingService.getDebitNoteConversionTemplate(this.organizationId, this.routePurchaseBillId).subscribe({
      next: (template) => {
        this.converting.set(false);
        this.pendingTemplateStore.setDebitNoteTemplate(template);
        this.router.navigate(['/organizations', this.organizationId, 'purchasing', 'debit-notes', 'new']);
      },
      error: (err: unknown) => {
        this.converting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not convert purchase bill to debit note.');
      },
    });
  }

  protected vatPercent(vatRate: VatRate): number {
    return vatRate === 'ThirteenPercentVat' ? 0.13 : 0;
  }

  private toLineInputs(): PurchaseBillLineInput[] | null {
    const lines = this.lines()
      .filter((l) => l.productId && l.quantity > 0)
      .map((l) => ({
        productId: l.productId,
        quantity: l.quantity,
        rate: l.rate,
        vatRate: l.vatRate,
        expenditureClassification: l.expenditureClassification,
      }));

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
    return { key: nextLineKey++, productId: '', quantity: 1, rate: 0, vatRate: 'NoVat', expenditureClassification: 'Others' };
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private round(value: number): number {
    return Math.round(value * 100) / 100;
  }

  private load(): void {
    this.loading.set(true);
    this.purchasingService.getPurchaseBill(this.organizationId, this.routePurchaseBillId).subscribe({
      next: (bill) => {
        this.purchaseBill.set(bill);
        this.contactId.set(bill.contactId);
        this.warehouseId.set(bill.warehouseId);
        this.date.set(bill.date);
        this.reference.set(bill.reference ?? '');
        this.supplierInvoiceReference.set(bill.supplierInvoiceReference ?? '');
        this.isImport.set(bill.isImport);
        this.importCountry.set(bill.importCountry ?? '');
        this.importDate.set(bill.importDate ?? '');
        this.importDocumentNo.set(bill.importDocumentNo ?? '');
        this.tdsTypeId.set(bill.tdsTypeId ?? '');
        this.referrerType = bill.referrerType;
        this.referrerId = bill.referrerId;
        this.lines.set(
          bill.lines.length > 0
            ? bill.lines.map((l) => ({
                key: nextLineKey++,
                productId: l.productId,
                quantity: l.quantity,
                rate: l.rate,
                vatRate: l.vatRate,
                expenditureClassification: l.expenditureClassification,
              }))
            : [this.newLine()],
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load purchase bill.');
      },
    });
  }
}
