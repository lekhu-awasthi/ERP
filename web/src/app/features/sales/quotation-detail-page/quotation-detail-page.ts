import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesService } from '../../../core/sales/sales.service';
import { QuotationDetail, QuotationLineInput } from '../../../core/sales/sales.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, VatRate } from '../../../core/catalog/catalog.models';
import { PendingTemplateStore } from '../../../core/sales/pending-template.store';
import { ReportingTagsEditor } from '../../../shared/reporting-tags/reporting-tags-editor';
import { CustomFieldsEditor } from '../../../shared/custom-fields/custom-fields-editor';
import { PrintingService } from '../../../core/printing/printing.service';
import { openBlankTabForPrint, openBlobInNewTab } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { DocumentTabs } from '../../../shared/document-tabs/document-tabs';

interface EditableLine {
  key: number;
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  discountPct: number;
}

let nextLineKey = 1;

/**
 * Clones journal-voucher-detail-page's transactional-document chrome (CLAUDE.md's Phase 5 brief):
 * a multi-line editable table (Product picker + Qty/Rate/VAT instead of Account/Debit/Credit),
 * running Line/VAT/Grand totals instead of a Debit/Credit Difference indicator, the same two-step
 * Draft-save vs Approve action, and the same route.paramMap subscription for the 'new' vs
 * ':quotationId' route-reuse gotcha (phase-3-status.md's bug #1).
 */
@Component({
  selector: 'app-quotation-detail-page',
  imports: [RouterLink, ReportingTagsEditor, CustomFieldsEditor, AmountPipe, BsDateInput, DocumentTabs],
  templateUrl: './quotation-detail-page.html',
})
export class QuotationDetailPage {
  private readonly customFieldsEditor = viewChild(CustomFieldsEditor);

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly salesService = inject(SalesService);
  private readonly contactsService = inject(ContactsService);
  private readonly catalogService = inject(CatalogService);
  private readonly pendingTemplateStore = inject(PendingTemplateStore);
  private readonly printingService = inject(PrintingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly converting = signal(false);
  protected readonly printing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly quotation = signal<QuotationDetail | null>(null);
  protected readonly customers = signal<Contact[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly date = signal(this.today());
  protected readonly expiryDate = signal('');
  protected readonly reference = signal('');
  protected readonly lines = signal<EditableLine[]>([]);
  protected readonly discountPct = signal(0);

  protected readonly vatRates: VatRate[] = ['NoVat', 'ZeroVat', 'ThirteenPercentVat'];

  protected routeQuotationId = '';

  /** See invoice-detail-page's identical Totals-panel doc comment. */
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
    const quotation = this.quotation();
    return this.isNew() || !quotation || quotation.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew() && lines.length >= 1 && lines.every((l) => l.productId && l.quantity > 0);
  });

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Customer').subscribe({ next: (c) => this.customers.set(c) });
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });

    this.route.paramMap.subscribe((params) => {
      this.routeQuotationId = params.get('quotationId')!;
      const isNew = this.routeQuotationId === 'new';
      this.isNew.set(isNew);
      this.quotation.set(null);
      this.errorMessage.set(null);

      if (isNew) {
        this.loading.set(false);
        this.contactId.set('');
        this.date.set(this.today());
        this.expiryDate.set('');
        this.reference.set('');
        this.discountPct.set(0);
        this.lines.set([this.newLine()]);
      } else {
        this.load();
      }
    });

    // Quick-action prefill (FR-4.6, roadmap Phase 18) -- subscribed after paramMap above so a
    // fresh 'new' navigation's form reset always runs before this applies the ?contactId= query
    // param, not after. Read reactively (not route.snapshot), per the route-reuse gotcha.
    this.route.queryParamMap.subscribe((params) => {
      const contactId = params.get('contactId');
      if (contactId && this.isNew()) {
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

  protected onProductChange(key: number, event: Event): void {
    const productId = (event.target as HTMLSelectElement).value;
    const product = this.products().find((p) => p.id === productId);
    this.updateLine(key, {
      productId,
      rate: product?.sellingPrice ?? 0,
      vatRate: product?.vatRate ?? 'NoVat',
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

    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const request = {
      contactId: this.contactId(),
      date: this.date(),
      expiryDate: this.expiryDate() || null,
      reference: this.reference() || null,
      lines,
      discountPct: this.discountPct(),
    };

    if (this.isNew()) {
      this.salesService.createQuotation(this.organizationId, request).subscribe({
        next: (result) => {
          this.customFieldsEditor()
            ?.commitTo(result.id)
            .subscribe({
              next: () => {
                this.saving.set(false);
                this.router.navigate(['/organizations', this.organizationId, 'sales', 'quotations', result.id]);
              },
              error: (err: unknown) => {
                this.saving.set(false);
                this.errorMessage.set(extractErrorMessage(err) ?? 'Quotation saved, but custom field values could not be saved.');
                this.router.navigate(['/organizations', this.organizationId, 'sales', 'quotations', result.id]);
              },
            });
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save quotation. Please try again.');
        },
      });
    } else {
      this.salesService.updateQuotation(this.organizationId, this.routeQuotationId, request).subscribe({
        next: () => {
          this.customFieldsEditor()
            ?.commitTo(this.routeQuotationId)
            .subscribe({
              next: () => {
                this.saving.set(false);
                this.load();
              },
              error: (err: unknown) => {
                this.saving.set(false);
                this.errorMessage.set(extractErrorMessage(err) ?? 'Quotation saved, but custom field values could not be saved.');
                this.load();
              },
            });
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save quotation. Please try again.');
        },
      });
    }
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.salesService.approveQuotation(this.organizationId, this.routeQuotationId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve quotation. Please try again.');
      },
    });
  }

  protected print(): void {
    this.printing.set(true);
    this.errorMessage.set(null);
    const tab = openBlankTabForPrint();

    this.printingService.printDocument(this.organizationId, 'Quotation', this.routeQuotationId).subscribe({
      next: (blob) => {
        this.printing.set(false);
        openBlobInNewTab(blob, tab);
      },
      error: (err: unknown) => {
        this.printing.set(false);
        tab?.close();
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print quotation. Please try again.');
      },
    });
  }

  protected voidQuotation(): void {
    if (!window.confirm('Void this quotation? This cannot be undone.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.salesService.voidQuotation(this.organizationId, this.routeQuotationId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void quotation. Please try again.');
      },
    });
  }

  protected convertToInvoice(): void {
    this.converting.set(true);
    this.errorMessage.set(null);

    this.salesService.getInvoiceConversionTemplate(this.organizationId, this.routeQuotationId).subscribe({
      next: (template) => {
        this.converting.set(false);
        this.pendingTemplateStore.setInvoiceTemplate(template);
        this.router.navigate(['/organizations', this.organizationId, 'sales', 'invoices', 'new']);
      },
      error: (err: unknown) => {
        this.converting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not convert quotation to invoice.');
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

  private toLineInputs(): QuotationLineInput[] | null {
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
    this.salesService.getQuotation(this.organizationId, this.routeQuotationId).subscribe({
      next: (quotation) => {
        this.quotation.set(quotation);
        this.contactId.set(quotation.contactId);
        this.date.set(quotation.date);
        this.expiryDate.set(quotation.expiryDate ?? '');
        this.reference.set(quotation.reference ?? '');
        this.discountPct.set(quotation.discountPct);
        this.lines.set(
          quotation.lines.length > 0
            ? quotation.lines.map((l) => ({
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
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load quotation.');
      },
    });
  }
}
