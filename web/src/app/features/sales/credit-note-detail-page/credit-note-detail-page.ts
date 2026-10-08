import { Component, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { BASE_CURRENCY_CODE } from '../../../core/organizations/organizations.models';
import { CurrencyRateFields } from '../../../shared/currency/currency-rate-fields';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesService } from '../../../core/sales/sales.service';
import { CreditableInvoice, CreditNoteDetail, CreditNoteLineInput, DocumentType } from '../../../core/sales/sales.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, VatRate } from '../../../core/catalog/catalog.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { PendingTemplateStore } from '../../../core/sales/pending-template.store';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { DocumentTabs } from '../../../shared/document-tabs/document-tabs';
import { ReportingTagsEditor } from '../../../shared/reporting-tags/reporting-tags-editor';
import { CustomFieldsEditor } from '../../../shared/custom-fields/custom-fields-editor';
import { commitCustomFieldsThen } from '../../../shared/custom-fields/commit-custom-fields';
import { PrintingService } from '../../../core/printing/printing.service';
import { openBlankTabForPrint, openBlobInNewTab } from '../../../shared/download-file';
import { TermsEditor } from '../../../shared/terms/terms-editor';
import { SendEmailDialog } from '../../../shared/send-email/send-email-dialog';
import { DocumentLocationPicker } from '../../../shared/locations/document-location-picker';
import { locationAwareProducts } from '../../../shared/catalog/location-aware-products';
import { FieldError, FieldErrorMessage } from '../../../shared/a11y/field-error';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { LineUnitControl, LineUnitOption } from '../../../shared/catalog/line-unit-control';
import { UnitOfMeasurementStore } from '../../../shared/catalog/unit-of-measurement-store';

interface EditableLine {
  key: number;
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  discountPct: number;

  /**
   * Phase 52 -- the unit this line is entered in. Empty means the product's own primary unit.
   * The conversion factor is not held here: the server resolves it from the catalogue and
   * freezes it on the line, so a client cannot claim a conversion the product does not have.
   */
  unitId: string;
}

let nextLineKey = 1;

/**
 * Phase 69 -- the invoice a standalone note names, as the picker shows it. `remainingTotal` is null for an
 * invoice read back from a saved draft, whose remainder the picker has not been asked for.
 */
interface PickedInvoice {
  id: string;
  code: string;
  date: string;
  currencyCode: string;
  grandTotal: number;
  remainingTotal: number | null;
}

/** Same chrome as invoice-detail-page (see that component's doc comment), minus the Warehouse
 * field -- CreditNote doesn't move stock this phase, same "planning document" treatment as
 * Quotation. Approve posts CreditNotePostingRule's exact reverse of InvoicePostingRule. */
@Component({
  selector: 'app-credit-note-detail-page',
  imports: [RouterLink, AmountPipe, BsDateInput, DocumentTabs, ReportingTagsEditor, CustomFieldsEditor, TermsEditor, CurrencyRateFields, SendEmailDialog, DocumentLocationPicker, StatusBanner, FieldErrorMessage, NepaliDatePipe, LineUnitControl],
  templateUrl: './credit-note-detail-page.html',
})
export class CreditNoteDetailPage {
  /** Phase 27a: custom field values ride the document's own Save. See
   * commitCustomFieldsThen for why the commit is an rxjs operator rather than a
   * nested subscribe, and why a failed commit does not report the save as failed. */
  private readonly customFieldsEditor = viewChild(CustomFieldsEditor);

  private readonly route = inject(ActivatedRoute);
  private readonly printingService = inject(PrintingService);
  private readonly router = inject(Router);
  private readonly salesService = inject(SalesService);
  private readonly contactsService = inject(ContactsService);
  private readonly catalogService = inject(CatalogService);
  private readonly accountingService = inject(AccountingService);
  private readonly pendingTemplateStore = inject(PendingTemplateStore);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  /** Phase 52 -- the tenant's units, shared and cached; a Product carries unit ids only. */
  protected readonly units = inject(UnitOfMeasurementStore).units(this.organizationId);

  protected readonly loading = signal(true);
  // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency, owned here
  // and rendered by the shared app-currency-rate-fields control.
  protected readonly currencyCode = signal(BASE_CURRENCY_CODE);
  protected readonly exchangeRate = signal(1);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /**
   * Phase 47 -- the form's per-field error state. Takes this page's own `errorMessage` signal rather
   * than owning a second one, so the banner and the field marking cannot disagree (see `FieldError`).
   */
  protected readonly fieldError = new FieldError(this.errorMessage);
  protected readonly creditNote = signal<CreditNoteDetail | null>(null);
  protected readonly customers = signal<Contact[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly date = signal(this.today());
  protected readonly reference = signal('');

  /**
   * Phase 35a (FR-2.3/FR-3.3) -- the billing location this document is raised from, shown by the
   * header picker `app-document-location-picker` renders. Empty means "let the server pick the
   * default", which `LocationResolver` turns into the tenant's HeadOffice, or into nothing when
   * this document type is outside the tenant's `LocationScopeMode`.
   */
  protected readonly locationId = signal('');
  protected readonly terms = signal('');
  protected readonly lines = signal<EditableLine[]>([]);
  protected readonly discountPct = signal(0);
  protected readonly isLinkedToSource = signal(false);
  private referrerType: DocumentType | null = null;
  private referrerId: string | null = null;

  /**
   * Phase 69 -- the tax invoice a standalone note relates to (VAT Rules Rule 20(1)(e)): one picked from
   * this system, or one issued before it, typed as a number and a date. A conversion names its invoice
   * already and shows it read-only. The server refuses Approve on a VAT-registered tenant while a note
   * names none, and refuses a picked invoice that is not this customer's, in this currency, at this
   * location, or with nothing left to credit (CreditNoteInvoiceReferences).
   */
  protected readonly invoiceMode = signal<'list' | 'typed'>('list');
  protected readonly pickedInvoice = signal<PickedInvoice | null>(null);
  protected readonly typedInvoiceNumber = signal('');
  protected readonly typedInvoiceDate = signal('');
  protected readonly invoiceSearch = signal('');
  protected readonly creditableInvoices = signal<CreditableInvoice[]>([]);
  private creditableRequest = 0;

  /** Phase 69 -- why the credit is given: optional on an ERP note, printed on the PDF when present. */
  protected readonly reason = signal('');

  /** The picker's options: the loaded page, with the picked invoice kept in front when a search or a
   * page boundary would otherwise drop it from under the selection. */
  protected readonly invoiceOptions = computed<PickedInvoice[]>(() => {
    const loaded: PickedInvoice[] = this.creditableInvoices().map((x) => ({
      id: x.id, code: x.code, date: x.date, currencyCode: x.currencyCode, grandTotal: x.grandTotal,
      remainingTotal: x.remainingTotal,
    }));
    const picked = this.pickedInvoice();
    return picked && !loaded.some((x) => x.id === picked.id) ? [picked, ...loaded] : loaded;
  });

  protected readonly vatRates: VatRate[] = ['NoVat', 'ZeroVat', 'ThirteenPercentVat'];

  protected readonly printing = signal(false);
  protected routeCreditNoteId = '';

  /** See invoice-detail-page's identical Totals-panel doc comment. */
  protected readonly subTotal = computed(() =>
    this.round(this.lines().reduce((sum, l) => sum + this.netAfterLineDiscount(l), 0)),
  );
  protected readonly discountAmount = computed(() => this.round((this.subTotal() * this.discountPct()) / 100));
  /** Phase 63 -- a till refund's own figures, or null for an ERP credit note. Created approved, so
   * never edited, and its totals are the server's stored ones -- for the invoice page's phase 61
   * reason: the computation below knows nothing of a service charge or a round-off. */
  protected readonly posRefund = computed(() => this.creditNote()?.posRefund ?? null);
  private readonly storedLines = computed(() => this.creditNote()?.lines ?? []);

  protected readonly nonTaxableTotal = computed(() =>
    this.posRefund()
      ? this.round(this.storedLines()
          .filter((l) => this.vatPercent(l.vatRate) === 0)
          .reduce((sum, l) => sum + l.amount + l.serviceChargeAmount, 0))
      : this.round(
          this.lines()
            .filter((l) => this.vatPercent(l.vatRate) === 0)
            .reduce((sum, l) => sum + this.netAfterBothDiscounts(l), 0),
        ),
  );
  protected readonly taxableTotal = computed(() =>
    this.posRefund()
      ? this.round(this.storedLines()
          .filter((l) => this.vatPercent(l.vatRate) > 0)
          .reduce((sum, l) => sum + l.amount + l.serviceChargeAmount, 0))
      : this.round(
          this.lines()
            .filter((l) => this.vatPercent(l.vatRate) > 0)
            .reduce((sum, l) => sum + this.netAfterBothDiscounts(l), 0),
        ),
  );
  protected readonly vatTotal = computed(() =>
    this.posRefund()
      ? this.round(this.storedLines().reduce((sum, l) => sum + l.vatAmount, 0))
      : this.round(this.lines().reduce((sum, l) => sum + this.netAfterBothDiscounts(l) * this.vatPercent(l.vatRate), 0)),
  );
  protected readonly grandTotal = computed(() => {
    const refund = this.posRefund();
    return refund ? refund.grandTotal : this.round(this.taxableTotal() + this.nonTaxableTotal() + this.vatTotal());
  });

  protected readonly isDraft = computed(() => {
    const creditNote = this.creditNote();
    return this.isNew() || !creditNote || creditNote.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew() && lines.length >= 1 && lines.every((l) => l.productId && l.quantity > 0);
  });

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Customer').subscribe({ next: (c) => this.customers.set(c) });
    // Phase 36 -- the picker lists the products available at THIS document's billing location,
    // and re-reads when the header location changes. See shared/catalog/location-aware-products.
    locationAwareProducts(this.organizationId, this.locationId, this.products);
    this.accountingService.listAllAccounts(this.organizationId).subscribe({ next: (a) => this.accounts.set(a) });

    // Phase 69 -- the picker follows the customer, the header location and the search. An effect because
    // the location can change from a control this page does not own; the load is untracked so that
    // writing its answer does not run the effect again.
    effect(() => {
      const contactId = this.contactId();
      const locationId = this.locationId();
      const search = this.invoiceSearch();
      const open = this.isDraft() && !this.isLinkedToSource() && this.invoiceMode() === 'list';
      untracked(() => this.loadCreditableInvoices(open ? contactId : '', locationId, search));
    });

    this.route.paramMap.subscribe((params) => {
      this.routeCreditNoteId = params.get('creditNoteId')!;
      const isNew = this.routeCreditNoteId === 'new';
      this.isNew.set(isNew);
      this.creditNote.set(null);
      this.errorMessage.set(null);
      this.referrerType = null;
      this.referrerId = null;
      this.isLinkedToSource.set(false);
      this.resetInvoiceReference();

      if (isNew) {
        this.loading.set(false);
        const template = this.pendingTemplateStore.takeCreditNoteTemplate();
        if (template) {
          this.contactId.set(template.contactId);
          this.date.set(template.date);
          this.reference.set(template.reference ?? '');
          // Phase 35a -- a conversion keeps the source document's branch. Without this the new
          // form's picker would fall back to the tenant default and move the document silently.
          this.locationId.set(template.locationId ?? '');
          // Phase 69 -- and its currency and rate: until now the template carried neither, so a foreign
          // source converted to a base-currency document at rate 1 (and the form kept whatever it last held).
          this.currencyCode.set(template.currencyCode);
          this.exchangeRate.set(template.exchangeRate);
          this.referrerType = template.referrerType;
          this.referrerId = template.referrerId;
          this.isLinkedToSource.set(true);
          // Phase 39 -- and it keeps the terms the invoice was issued under, for the same reason.
          this.terms.set(template.terms ?? '');
          this.discountPct.set(template.discountPct);
          this.lines.set(
            template.lines.length > 0
              ? template.lines.map((l) => ({ key: nextLineKey++, ...l, unitId: l.unitId ?? '' }))
              : [this.newLine()],
          );
        } else {
          this.contactId.set('');
          this.date.set(this.today());
          this.reference.set('');
        this.locationId.set('');
        this.currencyCode.set(BASE_CURRENCY_CODE);
        this.exchangeRate.set(1);
          this.terms.set('');
        this.terms.set('');
          this.discountPct.set(0);
          this.lines.set([this.newLine()]);
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

  /**
   * Phase 31 -- the rate now comes from the server, because which rate to suggest is a tenant
   * setting (SuggestSellingPriceMode: the most recent approved sale, or the product's own price) and
   * how to read the product's own price is a second one (ProductPriceBasis). Reading
   * `product.sellingPrice` here was silently the Fixed + Exclusive branch of both.
   *
   * The product's own price is applied immediately so the line never sits blank while the call is in
   * flight, then corrected when the answer arrives; a failed call leaves that fallback in place
   * rather than blocking the line.
   */
  protected onProductChange(key: number, event: Event): void {
    const productId = (event.target as HTMLSelectElement).value;
    const product = this.products().find((p) => p.id === productId);
    this.updateLine(key, { productId, rate: product?.sellingPrice ?? 0, vatRate: product?.vatRate ?? 'NoVat' });

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

  /** Phase 69 -- a different customer's invoices are not this note's to name. */
  protected onContactChange(event: Event): void {
    this.contactId.set((event.target as HTMLSelectElement).value);
    this.pickedInvoice.set(null);
  }

  /** Phase 69 -- nor are another branch's: a note is raised at its invoice's location. */
  protected onLocationChange(locationId: string): void {
    this.locationId.set(locationId);
    this.pickedInvoice.set(null);
  }

  protected setInvoiceMode(mode: 'list' | 'typed'): void {
    this.invoiceMode.set(mode);
    if (mode === 'typed') {
      this.pickedInvoice.set(null);
    } else {
      this.typedInvoiceNumber.set('');
      this.typedInvoiceDate.set('');
    }
  }

  /**
   * Phase 69 -- picking an invoice takes its currency and rate too: a price adjustment is in the
   * currency of the supply it adjusts, which the server enforces, so the currency control is locked
   * while an invoice is picked rather than left to disagree with it.
   */
  protected onInvoicePicked(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    const invoice = this.creditableInvoices().find((x) => x.id === id);
    if (!invoice) {
      this.pickedInvoice.set(null);
      return;
    }

    this.pickedInvoice.set({
      id: invoice.id, code: invoice.code, date: invoice.date, currencyCode: invoice.currencyCode,
      grandTotal: invoice.grandTotal, remainingTotal: invoice.remainingTotal,
    });
    this.currencyCode.set(invoice.currencyCode);
    this.exchangeRate.set(invoice.exchangeRate);
  }

  private loadCreditableInvoices(contactId: string, locationId: string, search: string): void {
    const request = ++this.creditableRequest;
    if (!contactId) {
      this.creditableInvoices.set([]);
      return;
    }

    this.salesService
      .listCreditableInvoices(
        this.organizationId, contactId, locationId || null, search || null, this.isNew() ? null : this.routeCreditNoteId,
      )
      .subscribe({
        // A slower answer for an earlier customer or search must not overwrite a newer one.
        next: (page) => {
          if (request === this.creditableRequest) {
            this.creditableInvoices.set(page.items);
          }
        },
        error: () => {
          if (request === this.creditableRequest) {
            this.creditableInvoices.set([]);
          }
        },
      });
  }

  private resetInvoiceReference(): void {
    this.invoiceMode.set('list');
    this.pickedInvoice.set(null);
    this.typedInvoiceNumber.set('');
    this.typedInvoiceDate.set('');
    this.invoiceSearch.set('');
    this.reason.set('');
  }

  protected addLine(): void {
    this.lines.update((lines) => [...lines, this.newLine()]);
  }

  protected removeLine(key: number): void {
    this.lines.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected saveDraft(): void {
    if (!this.contactId()) {
      this.fieldError.fail('credit-note-detail-page-customer', 'Select a Customer.');
      return;
    }

    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    // Phase 69 -- a typed invoice is its number and its date; one without the other names nothing.
    const typed = !this.isLinkedToSource() && this.invoiceMode() === 'typed';
    const typedNumber = typed ? this.typedInvoiceNumber().trim() : '';
    const typedDate = typed ? this.typedInvoiceDate() : '';
    if (Boolean(typedNumber) !== Boolean(typedDate)) {
      this.fieldError.fail('credit-note-detail-page-invoice-number', "Type both the invoice's number and its date.");
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const request = {

      currencyCode: this.currencyCode(),

      exchangeRate: this.exchangeRate(),
      contactId: this.contactId(),
      date: this.date(), locationId: this.locationId() || null,
      reference: this.reference() || null,
      terms: this.terms() || null,
      referrerType: this.referrerType,
      referrerId: this.referrerId,
      lines,
      discountPct: this.discountPct(),
      againstInvoiceId: !this.isLinkedToSource() && this.invoiceMode() === 'list' ? (this.pickedInvoice()?.id ?? null) : null,
      againstInvoiceNumber: typedNumber || null,
      againstInvoiceDate: typedDate || null,
      reason: this.reason().trim() || null,
    };

    if (this.isNew()) {
      this.salesService.createCreditNote(this.organizationId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), (r) => r.id, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: (result) => {
            this.saving.set(false);
            this.router.navigate(['/organizations', this.organizationId, 'sales', 'credit-notes', result.id]);
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save credit note. Please try again.');
          },
        });
    } else {
      this.salesService.updateCreditNote(this.organizationId, this.routeCreditNoteId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), () => this.routeCreditNoteId, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: () => {
            this.saving.set(false);
            this.load();
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save credit note. Please try again.');
          },
        });
    }
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.salesService.approveCreditNote(this.organizationId, this.routeCreditNoteId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve credit note. Please try again.');
      },
    });
  }

  protected voidCreditNote(): void {
    if (!window.confirm('Void this credit note? This reverses its GL posting and any restocked FIFO layer, and cannot be undone.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.salesService.voidCreditNote(this.organizationId, this.routeCreditNoteId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void credit note. Please try again.');
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

  private toLineInputs(): CreditNoteLineInput[] | null {
    const lines = this.lines()
      .filter((l) => l.productId && l.quantity > 0)
      // Phase 52 -- null rather than '' for the primary unit: the server reads null as "the
      // product's own primary unit", and an empty string is a value. This field is OPTIONAL on
      // the request record, so a form that forgot it would compile, pass every test, and save a
      // carton line as pieces -- which is phase 51's un-swept-increment bug in TypeScript.
      .map((l) => ({
        productId: l.productId,
        quantity: l.quantity,
        rate: l.rate,
        vatRate: l.vatRate,
        discountPct: l.discountPct,
        unitId: l.unitId || null,
      }));

    if (lines.length === 0) {
      this.fieldError.fail('credit-note-detail-page-add-line', 'Add at least one line with a Product and a Quantity.');
      return null;
    }

    return lines;
  }

  /**
   * Phase 52 -- picking a unit sets the Rate from that unit row's own price and nothing else.
   * Confirmed live 2026-09-17: Amount stays `Qty x Rate` in the entered unit, so the factor
   * never touches the money -- it is applied to the quantity, by the server, on the way into
   * the stock ledger.
   */
  protected onUnitChange(key: number, option: LineUnitOption): void {
    this.updateLine(key, { unitId: option.unitId, rate: option.sellingPrice });
  }

  /** Phase 52 -- the unit's short name for the read-only branch of the Qty cell. */
  protected unitLabel(line: EditableLine): string {
    const product = this.products().find((p) => p.id === line.productId);
    const unitId = line.unitId || product?.primaryUnitId;
    return this.units().find((u) => u.id === unitId)?.shortName ?? '';
  }

  private updateLine(key: number, patch: Partial<Omit<EditableLine, 'key'>>): void {
    this.lines.update((lines) => lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  }

  private newLine(): EditableLine {
    return { unitId: '',  key: nextLineKey++, productId: '', quantity: 1, rate: 0, vatRate: 'NoVat', discountPct: 0};
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private round(value: number): number {
    return Math.round(value * 100) / 100;
  }

  private load(): void {
    this.loading.set(true);
    this.salesService.getCreditNote(this.organizationId, this.routeCreditNoteId).subscribe({
      next: (creditNote) => {
        this.creditNote.set(creditNote);
        this.contactId.set(creditNote.contactId);
        this.date.set(creditNote.date);
        this.reference.set(creditNote.reference ?? '');
        this.locationId.set(creditNote.locationId ?? '');
        this.currencyCode.set(creditNote.currencyCode);
        this.exchangeRate.set(creditNote.exchangeRate);
        this.terms.set(creditNote.terms ?? '');
        this.referrerType = creditNote.referrerType;
        this.referrerId = creditNote.referrerId;
        this.isLinkedToSource.set(creditNote.referrerId !== null);
        // Phase 69 -- the invoice it names and why, read back so an edit does not clear them.
        this.reason.set(creditNote.reason ?? '');
        this.invoiceMode.set(creditNote.againstInvoiceNumber ? 'typed' : 'list');
        this.typedInvoiceNumber.set(creditNote.againstInvoiceNumber ?? '');
        this.typedInvoiceDate.set(creditNote.againstInvoiceDate ?? '');
        const named = creditNote.againstInvoiceId ? creditNote.relatedInvoice : null;
        this.pickedInvoice.set(
          named
            ? {
                id: named.id, code: named.code, date: named.date, currencyCode: named.currencyCode,
                grandTotal: named.grandTotal, remainingTotal: null,
              }
            : null,
        );
        this.discountPct.set(creditNote.discountPct);
        this.lines.set(
          creditNote.lines.length > 0
            ? creditNote.lines.map((l) => ({
                key: nextLineKey++,
                productId: l.productId,
                quantity: l.quantity,
                rate: l.rate,
                vatRate: l.vatRate,
                discountPct: l.discountPct,
                // Phase 52 -- read back, not only written: phase 35a's rule is write, READ, and
                // every prefill between, and the read half is the one no compiler checks.
                unitId: l.unitId ?? '',
              }))
            : [this.newLine()],
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load credit note.');
      },
    });
  }

  /** Phase 27b -- print/PDF, wired for this document type alongside the other eight the phase
   * added. Opens the tab synchronously before the request so the browser attributes it to the
   * click rather than blocking it as a popup. */
  protected print(): void {
    this.printing.set(true);
    this.errorMessage.set(null);
    const tab = openBlankTabForPrint();

    this.printingService.printDocument(this.organizationId, 'CreditNote', this.routeCreditNoteId).subscribe({
      next: (blob) => {
        this.printing.set(false);
        // Phase 67 -- the server wrote one print row; the note beside Print says so without a reload.
        this.creditNote.update((note) => (note ? { ...note, printCount: note.printCount + 1 } : note));
        openBlobInNewTab(blob, tab);
      },
      error: (err: unknown) => {
        this.printing.set(false);
        tab?.close();
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print credit note. Please try again.');
      },
    });
  }
}
