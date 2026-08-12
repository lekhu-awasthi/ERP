import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

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
import { PendingTemplateStore } from '../../../core/sales/pending-template.store';

interface EditableLine {
  key: number;
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate;
}

let nextLineKey = 1;

/** Mirror of credit-note-detail-page's chrome (see that component's doc comment) -- Supplier
 * picker instead of Customer, Purchase Account labels instead of Sales Account. Approve posts
 * DebitNotePostingRule's exact reverse of PurchaseBillPostingRule's non-TDS legs. */
@Component({
  selector: 'app-debit-note-detail-page',
  imports: [RouterLink, DatePipe],
  templateUrl: './debit-note-detail-page.html',
})
export class DebitNoteDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly purchasingService = inject(PurchasingService);
  private readonly contactsService = inject(ContactsService);
  private readonly catalogService = inject(CatalogService);
  private readonly accountingService = inject(AccountingService);
  private readonly pendingTemplateStore = inject(PendingTemplateStore);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly debitNote = signal<DebitNoteDetail | null>(null);
  protected readonly suppliers = signal<Contact[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly date = signal(this.today());
  protected readonly reference = signal('');
  protected readonly lines = signal<EditableLine[]>([]);
  private referrerType: DocumentType | null = null;
  private referrerId: string | null = null;

  protected readonly vatRates: VatRate[] = ['NoVat', 'ZeroVat', 'ThirteenPercentVat'];

  private routeDebitNoteId = '';

  protected readonly lineTotal = computed(() => this.round(this.lines().reduce((sum, l) => sum + l.quantity * l.rate, 0)));
  protected readonly vatTotal = computed(() =>
    this.round(this.lines().reduce((sum, l) => sum + l.quantity * l.rate * this.vatPercent(l.vatRate), 0)),
  );
  protected readonly grandTotal = computed(() => this.round(this.lineTotal() + this.vatTotal()));

  protected readonly isDraft = computed(() => {
    const debitNote = this.debitNote();
    return this.isNew() || !debitNote || debitNote.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew() && lines.length >= 1 && lines.every((l) => l.productId && l.quantity > 0);
  });

  constructor() {
    this.contactsService.listContacts(this.organizationId, 'Supplier').subscribe({ next: (c) => this.suppliers.set(c) });
    this.catalogService.listProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.accountingService.listAccounts(this.organizationId).subscribe({ next: (a) => this.accounts.set(a) });

    this.route.paramMap.subscribe((params) => {
      this.routeDebitNoteId = params.get('debitNoteId')!;
      const isNew = this.routeDebitNoteId === 'new';
      this.isNew.set(isNew);
      this.debitNote.set(null);
      this.errorMessage.set(null);
      this.referrerType = null;
      this.referrerId = null;

      if (isNew) {
        this.loading.set(false);
        const template = this.pendingTemplateStore.takeDebitNoteTemplate();
        if (template) {
          this.contactId.set(template.contactId);
          this.date.set(template.date);
          this.reference.set(template.reference ?? '');
          this.referrerType = template.referrerType;
          this.referrerId = template.referrerId;
          this.lines.set(
            template.lines.length > 0 ? template.lines.map((l) => ({ key: nextLineKey++, ...l })) : [this.newLine()],
          );
        } else {
          this.contactId.set('');
          this.date.set(this.today());
          this.reference.set('');
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
      contactId: this.contactId(),
      date: this.date(),
      reference: this.reference() || null,
      referrerType: this.referrerType,
      referrerId: this.referrerId,
      lines,
    };

    if (this.isNew()) {
      this.purchasingService.createDebitNote(this.organizationId, request).subscribe({
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
      this.purchasingService.updateDebitNote(this.organizationId, this.routeDebitNoteId, request).subscribe({
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

  private toLineInputs(): DebitNoteLineInput[] | null {
    const lines = this.lines()
      .filter((l) => l.productId && l.quantity > 0)
      .map((l) => ({ productId: l.productId, quantity: l.quantity, rate: l.rate, vatRate: l.vatRate }));

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
    return { key: nextLineKey++, productId: '', quantity: 1, rate: 0, vatRate: 'NoVat' };
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
        this.referrerType = debitNote.referrerType;
        this.referrerId = debitNote.referrerId;
        this.lines.set(
          debitNote.lines.length > 0
            ? debitNote.lines.map((l) => ({
                key: nextLineKey++,
                productId: l.productId,
                quantity: l.quantity,
                rate: l.rate,
                vatRate: l.vatRate,
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
}
