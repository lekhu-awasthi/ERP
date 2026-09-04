import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { BASE_CURRENCY_CODE } from '../../../core/organizations/organizations.models';
import { CurrencyRateFields } from '../../../shared/currency/currency-rate-fields';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { DebitNoteDetail, DebitNoteLineInput } from '../../../core/purchasing/purchasing.models';
import { DocumentType } from '../../../core/sales/sales.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product, VatRate } from '../../../core/catalog/catalog.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { TdsType } from '../../../core/configuration/configuration.models';
import { PendingTemplateStore } from '../../../core/sales/pending-template.store';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { DocumentTabs } from '../../../shared/document-tabs/document-tabs';
import { ReportingTagsEditor } from '../../../shared/reporting-tags/reporting-tags-editor';
import { CustomFieldsEditor } from '../../../shared/custom-fields/custom-fields-editor';
import { commitCustomFieldsThen } from '../../../shared/custom-fields/commit-custom-fields';
import { PrintingService } from '../../../core/printing/printing.service';
import { openBlankTabForPrint, openBlobInNewTab } from '../../../shared/download-file';

interface EditableLine {
  key: number;
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
  discountPct: number;
}

let nextLineKey = 1;

/** Mirror of credit-note-detail-page's chrome (see that component's doc comment) -- Supplier
 * picker instead of Customer, Purchase Account labels instead of Sales Account. Approve posts
 * DebitNotePostingRule's exact reverse of PurchaseBillPostingRule, TDS leg included -- TdsTypeId
 * is pre-filled from the source PurchaseBill on conversion but user-editable (a partial-quantity
 * debit note recomputes TdsAmount server-side from its own lines, same as PurchaseBill/Expense),
 * so a full reversal nets Accounts Payable and TDS Payable back to zero. */
@Component({
  selector: 'app-debit-note-detail-page',
  imports: [RouterLink, DatePipe, AmountPipe, BsDateInput, DocumentTabs, ReportingTagsEditor, CustomFieldsEditor, CurrencyRateFields],
  templateUrl: './debit-note-detail-page.html',
})
export class DebitNoteDetailPage {
  /** Phase 27a: custom field values ride the document's own Save. See
   * commitCustomFieldsThen for why the commit is an rxjs operator rather than a
   * nested subscribe, and why a failed commit does not report the save as failed. */
  private readonly customFieldsEditor = viewChild(CustomFieldsEditor);

  private readonly route = inject(ActivatedRoute);
  private readonly printingService = inject(PrintingService);
  private readonly router = inject(Router);
  private readonly purchasingService = inject(PurchasingService);
  private readonly contactsService = inject(ContactsService);
  private readonly catalogService = inject(CatalogService);
  private readonly accountingService = inject(AccountingService);
  private readonly configurationService = inject(ConfigurationService);
  private readonly pendingTemplateStore = inject(PendingTemplateStore);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency, owned here
  // and rendered by the shared app-currency-rate-fields control.
  protected readonly currencyCode = signal(BASE_CURRENCY_CODE);
  protected readonly exchangeRate = signal(1);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly debitNote = signal<DebitNoteDetail | null>(null);
  protected readonly suppliers = signal<Contact[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly tdsTypes = signal<TdsType[]>([]);
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly date = signal(this.today());
  protected readonly reference = signal('');
  protected readonly tdsTypeId = signal('');
  protected readonly lines = signal<EditableLine[]>([]);
  protected readonly discountPct = signal(0);
  protected readonly isLinkedToSource = signal(false);
  private referrerType: DocumentType | null = null;
  private referrerId: string | null = null;

  protected readonly vatRates: VatRate[] = ['NoVat', 'ZeroVat', 'ThirteenPercentVat'];

  protected readonly printing = signal(false);
  protected routeDebitNoteId = '';

  /** See Sales' invoice-detail-page identical Totals-panel doc comment. */
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
    const debitNote = this.debitNote();
    return this.isNew() || !debitNote || debitNote.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew() && lines.length >= 1 && lines.every((l) => l.productId && l.quantity > 0);
  });

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Supplier').subscribe({ next: (c) => this.suppliers.set(c) });
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.accountingService.listAllAccounts(this.organizationId).subscribe({ next: (a) => this.accounts.set(a) });
    this.configurationService.listTdsTypes(this.organizationId).subscribe({ next: (t) => this.tdsTypes.set(t) });

    this.route.paramMap.subscribe((params) => {
      this.routeDebitNoteId = params.get('debitNoteId')!;
      const isNew = this.routeDebitNoteId === 'new';
      this.isNew.set(isNew);
      this.debitNote.set(null);
      this.errorMessage.set(null);
      this.referrerType = null;
      this.referrerId = null;
      this.isLinkedToSource.set(false);

      if (isNew) {
        this.loading.set(false);
        const template = this.pendingTemplateStore.takeDebitNoteTemplate();
        if (template) {
          this.contactId.set(template.contactId);
          this.date.set(template.date);
          this.reference.set(template.reference ?? '');
          this.tdsTypeId.set(template.tdsTypeId ?? '');
          this.referrerType = template.referrerType;
          this.referrerId = template.referrerId;
          this.isLinkedToSource.set(true);
          this.discountPct.set(template.discountPct);
          this.lines.set(
            template.lines.length > 0 ? template.lines.map((l) => ({ key: nextLineKey++, ...l })) : [this.newLine()],
          );
        } else {
          this.contactId.set('');
          this.date.set(this.today());
          this.reference.set('');
          this.currencyCode.set(BASE_CURRENCY_CODE);
          this.exchangeRate.set(1);
          this.tdsTypeId.set('');
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
      this.errorMessage.set('Select a Supplier.');
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
      reference: this.reference() || null,
      tdsTypeId: this.tdsTypeId() || null,
      referrerType: this.referrerType,
      referrerId: this.referrerId,
      lines,
      discountPct: this.discountPct(),
    };

    if (this.isNew()) {
      this.purchasingService.createDebitNote(this.organizationId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), (r) => r.id, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: (result) => {
            this.saving.set(false);
            this.router.navigate(['/organizations', this.organizationId, 'purchasing', 'debit-notes', result.id]);
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save debit note. Please try again.');
          },
        });
    } else {
      this.purchasingService.updateDebitNote(this.organizationId, this.routeDebitNoteId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), () => this.routeDebitNoteId, (m) => this.errorMessage.set(m)))
        .subscribe({
          next: () => {
            this.saving.set(false);
            this.load();
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save debit note. Please try again.');
          },
        });
    }
  }

  protected voidDebitNote(): void {
    if (!window.confirm('Void this debit note? This reverses its GL posting and restocks any consumed FIFO layer, and cannot be undone.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.purchasingService.voidDebitNote(this.organizationId, this.routeDebitNoteId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void debit note. Please try again.');
      },
    });
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.purchasingService.approveDebitNote(this.organizationId, this.routeDebitNoteId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve debit note. Please try again.');
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

  private toLineInputs(): DebitNoteLineInput[] | null {
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
    this.purchasingService.getDebitNote(this.organizationId, this.routeDebitNoteId).subscribe({
      next: (debitNote) => {
        this.debitNote.set(debitNote);
        this.contactId.set(debitNote.contactId);
        this.date.set(debitNote.date);
        this.reference.set(debitNote.reference ?? '');
        this.currencyCode.set(debitNote.currencyCode);
        this.exchangeRate.set(debitNote.exchangeRate);
        this.tdsTypeId.set(debitNote.tdsTypeId ?? '');
        this.referrerType = debitNote.referrerType;
        this.referrerId = debitNote.referrerId;
        this.isLinkedToSource.set(debitNote.referrerId !== null);
        this.discountPct.set(debitNote.discountPct);
        this.lines.set(
          debitNote.lines.length > 0
            ? debitNote.lines.map((l) => ({
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
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load debit note.');
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

    this.printingService.printDocument(this.organizationId, 'DebitNote', this.routeDebitNoteId).subscribe({
      next: (blob) => {
        this.printing.set(false);
        openBlobInNewTab(blob, tab);
      },
      error: (err: unknown) => {
        this.printing.set(false);
        tab?.close();
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print debit note. Please try again.');
      },
    });
  }
}
