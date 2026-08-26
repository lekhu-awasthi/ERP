import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesService } from '../../../core/sales/sales.service';
import { DocumentType, InvoiceDetail, InvoiceLineInput } from '../../../core/sales/sales.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, VatRate } from '../../../core/catalog/catalog.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';
import { PendingTemplateStore } from '../../../core/sales/pending-template.store';
import { ReportingTagsEditor } from '../../../shared/reporting-tags/reporting-tags-editor';
import { CustomFieldsEditor } from '../../../shared/custom-fields/custom-fields-editor';

interface EditableLine {
  key: number;
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  discountPct: number;
}

let nextLineKey = 1;

/** Clones journal-voucher-detail-page's chrome (see quotation-detail-page's doc comment for the
 * full rationale) -- adds a required Warehouse field (first aggregate in this codebase with one)
 * and a live GL-preview section before Approve, since Invoice's posting isn't a 1:1 mirror of its
 * own lines the way JournalVoucher's is. */
@Component({
  selector: 'app-invoice-detail-page',
  imports: [RouterLink, DatePipe, ReportingTagsEditor, CustomFieldsEditor],
  templateUrl: './invoice-detail-page.html',
})
export class InvoiceDetailPage {
  private readonly customFieldsEditor = viewChild(CustomFieldsEditor);

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly salesService = inject(SalesService);
  private readonly contactsService = inject(ContactsService);
  private readonly catalogService = inject(CatalogService);
  private readonly accountingService = inject(AccountingService);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly pendingTemplateStore = inject(PendingTemplateStore);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly converting = signal(false);
  protected readonly voiding = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly invoice = signal<InvoiceDetail | null>(null);
  protected readonly customers = signal<Contact[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly warehouseId = signal('');
  protected readonly date = signal(this.today());
  protected readonly reference = signal('');
  protected readonly lines = signal<EditableLine[]>([]);
  protected readonly discountPct = signal(0);
  private referrerType: DocumentType | null = null;
  private referrerId: string | null = null;

  protected readonly vatRates: VatRate[] = ['NoVat', 'ZeroVat', 'ThirteenPercentVat'];

  private routeInvoiceId = '';

  /** Confirmed live Totals panel order: Sub Total (net of each line's own Discount%) -> header
   * Discount% -> Non-Taxable/Taxable split -> VAT -> Grand Total. Every line's net-of-both-discounts
   * share is what VAT is computed on -- see InvoiceLine.Create's doc comment for the same formula
   * applied server-side. */
  protected readonly subTotal = computed(() =>
    this.round(this.lines().reduce((sum, l) => sum + this.netAfterLineDiscount(l), 0)),
  );
  protected readonly discountAmount = computed(() => this.round((this.subTotal() * this.discountPct()) / 100));
  protected readonly nonTaxableTotal = computed(() =>
    this.round(
      this.lines()
        .filter((l) => this.vatPercent(l.vatRate) === 0)
        .reduce((sum, l) => sum + this.netAfterBothDiscounts(l), 0),
    ),
  );
  protected readonly taxableTotal = computed(() =>
    this.round(
      this.lines()
        .filter((l) => this.vatPercent(l.vatRate) > 0)
        .reduce((sum, l) => sum + this.netAfterBothDiscounts(l), 0),
    ),
  );
  protected readonly vatTotal = computed(() =>
    this.round(this.lines().reduce((sum, l) => sum + this.netAfterBothDiscounts(l) * this.vatPercent(l.vatRate), 0)),
  );
  protected readonly grandTotal = computed(() => this.round(this.taxableTotal() + this.nonTaxableTotal() + this.vatTotal()));

  protected readonly isDraft = computed(() => {
    const invoice = this.invoice();
    return this.isNew() || !invoice || invoice.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew() && lines.length >= 1 && lines.every((l) => l.productId && l.quantity > 0) && !!this.warehouseId();
  });

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Customer').subscribe({ next: (c) => this.customers.set(c) });
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.accountingService.listAllAccounts(this.organizationId).subscribe({ next: (a) => this.accounts.set(a) });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({ next: (w) => this.warehouses.set(w) });

    this.route.paramMap.subscribe((params) => {
      this.routeInvoiceId = params.get('invoiceId')!;
      const isNew = this.routeInvoiceId === 'new';
      this.isNew.set(isNew);
      this.invoice.set(null);
      this.errorMessage.set(null);
      this.referrerType = null;
      this.referrerId = null;

      if (isNew) {
        this.loading.set(false);
        const template = this.pendingTemplateStore.takeInvoiceTemplate();
        if (template) {
          this.contactId.set(template.contactId);
          this.date.set(template.date);
          this.reference.set(template.reference ?? '');
          this.referrerType = template.referrerType;
          this.referrerId = template.referrerId;
          this.discountPct.set(template.discountPct);
          this.lines.set(
            template.lines.length > 0
              ? template.lines.map((l) => ({ key: nextLineKey++, ...l }))
              : [this.newLine()],
          );
        } else {
          this.contactId.set('');
          this.date.set(this.today());
          this.reference.set('');
          this.discountPct.set(0);
          this.lines.set([this.newLine()]);
        }
        this.warehouseId.set('');
      } else {
        this.load();
      }
    });

    // Quick-action prefill (FR-4.6, roadmap Phase 18) -- subscribed after paramMap above so a
    // fresh 'new' navigation's form reset always runs before this applies the ?contactId= query
    // param, not after. Read reactively (not route.snapshot), per the route-reuse gotcha. Only
    // applies when there's no pending conversion template (that template already carries its own
    // contactId, and takes precedence over a quick-action query param).
    this.route.queryParamMap.subscribe((params) => {
      const contactId = params.get('contactId');
      if (contactId && this.isNew() && !this.referrerId) {
        this.contactId.set(contactId);
      }
    });
  }

  protected contactLabel(contactId: string): string {
    const contact = this.customers().find((c) => c.id === contactId);
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

  protected onProductChange(key: number, event: Event): void {
    const productId = (event.target as HTMLSelectElement).value;
    const product = this.products().find((p) => p.id === productId);
    this.updateLine(key, { productId, rate: product?.sellingPrice ?? 0, vatRate: product?.vatRate ?? 'NoVat' });
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

  protected onDiscountPctChange(key: number, event: Event): void {
    const discountPct = (event.target as HTMLInputElement).valueAsNumber;
    this.updateLine(key, { discountPct: Number.isFinite(discountPct) ? discountPct : 0 });
  }

  protected onHeaderDiscountPctChange(event: Event): void {
    const discountPct = (event.target as HTMLInputElement).valueAsNumber;
    this.discountPct.set(Number.isFinite(discountPct) ? discountPct : 0);
  }

  protected addLine(): void {
    this.lines.update((lines) => [...lines, this.newLine()]);
  }

  protected removeLine(key: number): void {
    this.lines.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected saveDraft(): void {
    if (!this.contactId()) {
      this.errorMessage.set('Select a Customer.');
      return;
    }
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
      contactId: this.contactId(),
      warehouseId: this.warehouseId(),
      date: this.date(),
      reference: this.reference() || null,
      referrerType: this.referrerType,
      referrerId: this.referrerId,
      lines,
      discountPct: this.discountPct(),
    };

    if (this.isNew()) {
      this.salesService.createInvoice(this.organizationId, request).subscribe({
        next: (result) => {
          this.customFieldsEditor()
            ?.commitTo(result.id)
            .subscribe({
              next: () => {
                this.saving.set(false);
                this.router.navigate(['/organizations', this.organizationId, 'sales', 'invoices', result.id]);
              },
              error: (err: unknown) => {
                this.saving.set(false);
                this.errorMessage.set(extractErrorMessage(err) ?? 'Invoice saved, but custom field values could not be saved.');
                this.router.navigate(['/organizations', this.organizationId, 'sales', 'invoices', result.id]);
              },
            });
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save invoice. Please try again.');
        },
      });
    } else {
      this.salesService.updateInvoice(this.organizationId, this.routeInvoiceId, request).subscribe({
        next: () => {
          this.customFieldsEditor()
            ?.commitTo(this.routeInvoiceId)
            .subscribe({
              next: () => {
                this.saving.set(false);
                this.load();
              },
              error: (err: unknown) => {
                this.saving.set(false);
                this.errorMessage.set(extractErrorMessage(err) ?? 'Invoice saved, but custom field values could not be saved.');
                this.load();
              },
            });
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save invoice. Please try again.');
        },
      });
    }
  }

  /** overrideWarning=true is only ever passed by the confirm-dialog resubmit below --
   * architecture-spec.md §3.5's Warn-and-allow flow, avoiding a second round-trip just to ask
   * "are you sure". A 422 means the API is showing a StockAvailabilityWarningException (a
   * confirmable warning, not a hard block) -- distinct from every other status code, which is a
   * real error the user can't route around by confirming. */
  protected approve(overrideWarning = false): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.salesService.approveInvoice(this.organizationId, this.routeInvoiceId, overrideWarning).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);

        if (err instanceof HttpErrorResponse && err.status === 422) {
          const message = extractErrorMessage(err) ?? 'This invoice exceeds the available stock.';
          if (window.confirm(`${message}\n\nApprove anyway?`)) {
            this.approve(true);
            return;
          }
          this.errorMessage.set(message);
          return;
        }

        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve invoice. Please try again.');
      },
    });
  }

  protected convertToCreditNote(): void {
    this.converting.set(true);
    this.errorMessage.set(null);

    this.salesService.getCreditNoteConversionTemplate(this.organizationId, this.routeInvoiceId).subscribe({
      next: (template) => {
        this.converting.set(false);
        this.pendingTemplateStore.setCreditNoteTemplate(template);
        this.router.navigate(['/organizations', this.organizationId, 'sales', 'credit-notes', 'new']);
      },
      error: (err: unknown) => {
        this.converting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not convert invoice to credit note.');
      },
    });
  }

  protected voidInvoice(): void {
    if (!window.confirm('Void this invoice? This reverses its GL posting and restores consumed stock, and cannot be undone.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.salesService.voidInvoice(this.organizationId, this.routeInvoiceId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void invoice. Please try again.');
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

  private toLineInputs(): InvoiceLineInput[] | null {
    const lines = this.lines()
      .filter((l) => l.productId && l.quantity > 0)
      .map((l) => ({ productId: l.productId, quantity: l.quantity, rate: l.rate, vatRate: l.vatRate, discountPct: l.discountPct }));

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
    return { key: nextLineKey++, productId: '', quantity: 1, rate: 0, vatRate: 'NoVat', discountPct: 0 };
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private round(value: number): number {
    return Math.round(value * 100) / 100;
  }

  private load(): void {
    this.loading.set(true);
    this.salesService.getInvoice(this.organizationId, this.routeInvoiceId).subscribe({
      next: (invoice) => {
        this.invoice.set(invoice);
        this.contactId.set(invoice.contactId);
        this.warehouseId.set(invoice.warehouseId);
        this.date.set(invoice.date);
        this.reference.set(invoice.reference ?? '');
        this.referrerType = invoice.referrerType;
        this.referrerId = invoice.referrerId;
        this.discountPct.set(invoice.discountPct);
        this.lines.set(
          invoice.lines.length > 0
            ? invoice.lines.map((l) => ({
                key: nextLineKey++,
                productId: l.productId,
                quantity: l.quantity,
                rate: l.rate,
                vatRate: l.vatRate,
                discountPct: l.discountPct,
              }))
            : [this.newLine()],
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load invoice.');
      },
    });
  }
}
