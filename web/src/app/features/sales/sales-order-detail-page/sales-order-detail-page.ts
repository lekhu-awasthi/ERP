import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { BASE_CURRENCY_CODE } from '../../../core/organizations/organizations.models';
import { CurrencyRateFields } from '../../../shared/currency/currency-rate-fields';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesService } from '../../../core/sales/sales.service';
import { SalesOrderDetail, SalesOrderLineInput } from '../../../core/sales/sales.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, VatRate } from '../../../core/catalog/catalog.models';
import { PrintingService } from '../../../core/printing/printing.service';
import { openBlankTabForPrint, openBlobInNewTab } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { DocumentTabs } from '../../../shared/document-tabs/document-tabs';
import { ReportingTagsEditor } from '../../../shared/reporting-tags/reporting-tags-editor';
import { CustomFieldsEditor } from '../../../shared/custom-fields/custom-fields-editor';
import { commitCustomFieldsThen } from '../../../shared/custom-fields/commit-custom-fields';
import { TermsEditor } from '../../../shared/terms/terms-editor';
import { SendEmailDialog } from '../../../shared/send-email/send-email-dialog';

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
 * Clones quotation-detail-page's chrome exactly (roadmap Phase 18 -- Sales Order had zero Angular
 * UI through Phase 16b, see CLAUDE.md's phase-18 brief). Same Draft->Approve->Void lifecycle, same
 * line-item editor shape; Sales Order has no "Convert to X" flow (no conversion-template endpoint
 * exists server-side), so unlike Quotation there's no Convert button here -- keep this minimal,
 * matching Quotation's feature set exactly, not gold-plating.
 *
 * Also the "Create Sales Order" quick action's landing target (CLAUDE.md's phase-18 brief, FR-4.6)
 * -- reads route.queryParamMap reactively (not snapshot) for an optional ?contactId= param and
 * prefills the Customer picker once the Customer list has loaded, same route-reuse discipline as
 * the route.paramMap subscription below (phase-3-status.md's bug #1).
 */
@Component({
  selector: 'app-sales-order-detail-page',
  imports: [RouterLink, AmountPipe, BsDateInput, DocumentTabs, ReportingTagsEditor, CustomFieldsEditor, TermsEditor, CurrencyRateFields, SendEmailDialog],
  templateUrl: './sales-order-detail-page.html',
})
export class SalesOrderDetailPage {
  /** Phase 27a: custom field values ride the document's own Save. See
   * commitCustomFieldsThen for why the commit is an rxjs operator rather than a
   * nested subscribe, and why a failed commit does not report the save as failed. */
  private readonly customFieldsEditor = viewChild(CustomFieldsEditor);

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly salesService = inject(SalesService);
  private readonly contactsService = inject(ContactsService);
  private readonly catalogService = inject(CatalogService);
  private readonly printingService = inject(PrintingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency, owned here
  // and rendered by the shared app-currency-rate-fields control.
  protected readonly currencyCode = signal(BASE_CURRENCY_CODE);
  protected readonly exchangeRate = signal(1);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly printing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly salesOrder = signal<SalesOrderDetail | null>(null);
  protected readonly customers = signal<Contact[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly date = signal(this.today());
  protected readonly deliveryDate = signal('');
  protected readonly reference = signal('');
  protected readonly terms = signal('');
  protected readonly lines = signal<EditableLine[]>([]);
  protected readonly discountPct = signal(0);

  protected readonly vatRates: VatRate[] = ['NoVat', 'ZeroVat', 'ThirteenPercentVat'];

  protected routeSalesOrderId = '';

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
    const salesOrder = this.salesOrder();
    return this.isNew() || !salesOrder || salesOrder.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew() && lines.length >= 1 && lines.every((l) => l.productId && l.quantity > 0);
  });

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Customer').subscribe({ next: (c) => this.customers.set(c) });
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });

    this.route.paramMap.subscribe((params) => {
      this.routeSalesOrderId = params.get('salesOrderId')!;
      const isNew = this.routeSalesOrderId === 'new';
      this.isNew.set(isNew);
      this.salesOrder.set(null);
      this.errorMessage.set(null);

      if (isNew) {
        this.loading.set(false);
        this.contactId.set('');
        this.date.set(this.today());
        this.deliveryDate.set('');
        this.reference.set('');
        this.currencyCode.set(BASE_CURRENCY_CODE);
        this.exchangeRate.set(1);
        this.terms.set('');
        this.discountPct.set(0);
        this.lines.set([this.newLine()]);
      } else {
        this.load();
      }
    });

    // Quick-action prefill (FR-4.6) -- subscribed after paramMap above so a fresh 'new' navigation's
    // form reset (above) always runs before this applies the ?contactId= query param, not after.
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

      currencyCode: this.currencyCode(),

      exchangeRate: this.exchangeRate(),
      contactId: this.contactId(),
      date: this.date(),
      deliveryDate: this.deliveryDate() || null,
      reference: this.reference() || null,
      terms: this.terms() || null,
      lines,
      discountPct: this.discountPct(),
    };

    if (this.isNew()) {
      this.salesService.createSalesOrder(this.organizationId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), (r) => r.id, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: (result) => {
            this.saving.set(false);
            this.router.navigate(['/organizations', this.organizationId, 'sales', 'sales-orders', result.id]);
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save sales order. Please try again.');
          },
        });
    } else {
      this.salesService.updateSalesOrder(this.organizationId, this.routeSalesOrderId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), () => this.routeSalesOrderId, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: () => {
            this.saving.set(false);
            this.load();
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save sales order. Please try again.');
          },
        });
    }
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.salesService.approveSalesOrder(this.organizationId, this.routeSalesOrderId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve sales order. Please try again.');
      },
    });
  }

  protected print(): void {
    this.printing.set(true);
    this.errorMessage.set(null);
    const tab = openBlankTabForPrint();

    this.printingService.printDocument(this.organizationId, 'SalesOrder', this.routeSalesOrderId).subscribe({
      next: (blob) => {
        this.printing.set(false);
        openBlobInNewTab(blob, tab);
      },
      error: (err: unknown) => {
        this.printing.set(false);
        tab?.close();
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print sales order. Please try again.');
      },
    });
  }

  protected voidSalesOrder(): void {
    if (!window.confirm('Void this sales order? This cannot be undone.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.salesService.voidSalesOrder(this.organizationId, this.routeSalesOrderId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void sales order. Please try again.');
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

  private toLineInputs(): SalesOrderLineInput[] | null {
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
    this.salesService.getSalesOrder(this.organizationId, this.routeSalesOrderId).subscribe({
      next: (salesOrder) => {
        this.salesOrder.set(salesOrder);
        this.contactId.set(salesOrder.contactId);
        this.date.set(salesOrder.date);
        this.deliveryDate.set(salesOrder.deliveryDate ?? '');
        this.reference.set(salesOrder.reference ?? '');
        this.currencyCode.set(salesOrder.currencyCode);
        this.exchangeRate.set(salesOrder.exchangeRate);
        this.terms.set(salesOrder.terms ?? '');
        this.discountPct.set(salesOrder.discountPct);
        this.lines.set(
          salesOrder.lines.length > 0
            ? salesOrder.lines.map((l) => ({
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
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load sales order.');
      },
    });
  }
}
