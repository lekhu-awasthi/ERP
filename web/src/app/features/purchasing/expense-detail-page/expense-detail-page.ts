import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { ExpenseDetail, ExpenseLineInput } from '../../../core/purchasing/purchasing.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { VatRate } from '../../../core/catalog/catalog.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { TdsType } from '../../../core/configuration/configuration.models';

interface EditableLine {
  key: number;
  accountId: string;
  amount: number;
  vatRate: VatRate;
}

let nextLineKey = 1;

/**
 * NOT a clone of purchase-bill-detail-page -- confirmed live as its own document type with an
 * "Accounts" line-item table (Select Account, Amount, VAT) instead of Product lines, each line
 * debiting a GL account directly. Closer to journal-voucher-detail-page's line-table shape than
 * invoice-detail-page's Product-picker one, but keeps the same header chrome (Supplier, Date, Due
 * Date, Supplier Invoice Reference, Notes, TDS toggle + Type) and two-step Draft-save vs Approve
 * action.
 */
@Component({
  selector: 'app-expense-detail-page',
  imports: [RouterLink, DatePipe],
  templateUrl: './expense-detail-page.html',
})
export class ExpenseDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly purchasingService = inject(PurchasingService);
  private readonly contactsService = inject(ContactsService);
  private readonly accountingService = inject(AccountingService);
  private readonly configurationService = inject(ConfigurationService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly previewingGl = signal(false);
  protected readonly glPreview = signal<{ accountId: string; debit: number; credit: number }[] | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly expense = signal<ExpenseDetail | null>(null);
  protected readonly suppliers = signal<Contact[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly tdsTypes = signal<TdsType[]>([]);
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly date = signal(this.today());
  protected readonly dueDate = signal('');
  protected readonly supplierInvoiceReference = signal('');
  protected readonly notes = signal('');
  protected readonly tdsApplicable = signal(false);
  protected readonly tdsTypeId = signal('');
  protected readonly lines = signal<EditableLine[]>([]);

  protected readonly vatRates: VatRate[] = ['NoVat', 'ZeroVat', 'ThirteenPercentVat'];

  private routeExpenseId = '';

  protected readonly sortedAccounts = computed(() => [...this.accounts()].sort((a, b) => a.code.localeCompare(b.code)));

  protected readonly lineTotal = computed(() => this.round(this.lines().reduce((sum, l) => sum + l.amount, 0)));
  protected readonly vatTotal = computed(() =>
    this.round(this.lines().reduce((sum, l) => sum + l.amount * this.vatPercent(l.vatRate), 0)),
  );
  protected readonly grandTotal = computed(() => this.round(this.lineTotal() + this.vatTotal()));

  protected readonly isDraft = computed(() => {
    const expense = this.expense();
    return this.isNew() || !expense || expense.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return !this.isNew() && lines.length >= 1 && lines.every((l) => l.accountId && l.amount > 0);
  });

  constructor() {
    this.contactsService.listContacts(this.organizationId, 'Supplier').subscribe({ next: (c) => this.suppliers.set(c) });
    this.accountingService.listAccounts(this.organizationId).subscribe({ next: (a) => this.accounts.set(a) });
    this.configurationService.listTdsTypes(this.organizationId).subscribe({ next: (t) => this.tdsTypes.set(t) });

    this.route.paramMap.subscribe((params) => {
      this.routeExpenseId = params.get('expenseId')!;
      const isNew = this.routeExpenseId === 'new';
      this.isNew.set(isNew);
      this.expense.set(null);
      this.errorMessage.set(null);

      if (isNew) {
        this.loading.set(false);
        this.contactId.set('');
        this.date.set(this.today());
        this.dueDate.set('');
        this.supplierInvoiceReference.set('');
        this.notes.set('');
        this.tdsApplicable.set(false);
        this.tdsTypeId.set('');
        this.lines.set([this.newLine()]);
      } else {
        this.load();
      }
    });
  }

  protected contactLabel(contactId: string): string {
    const contact = this.suppliers().find((c) => c.id === contactId);
    return contact ? `${contact.code} — ${contact.name}` : '—';
  }

  protected accountLabel(accountId: string): string {
    const account = this.accounts().find((a) => a.id === accountId);
    return account ? `${account.code} — ${account.name}` : '—';
  }

  protected onAccountChange(key: number, event: Event): void {
    const accountId = (event.target as HTMLSelectElement).value;
    this.updateLine(key, { accountId });
  }

  protected onAmountChange(key: number, event: Event): void {
    const amount = (event.target as HTMLInputElement).valueAsNumber;
    this.updateLine(key, { amount: Number.isFinite(amount) ? amount : 0 });
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

  protected previewGlPosting(): void {
    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    this.previewingGl.set(true);
    this.errorMessage.set(null);

    this.purchasingService
      .previewExpenseGlPosting(this.organizationId, lines, this.tdsApplicable(), this.tdsTypeId() || null)
      .subscribe({
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

    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const request = {
      contactId: this.contactId(),
      date: this.date(),
      dueDate: this.dueDate() || null,
      supplierInvoiceReference: this.supplierInvoiceReference() || null,
      notes: this.notes() || null,
      tdsApplicable: this.tdsApplicable(),
      tdsTypeId: this.tdsApplicable() ? this.tdsTypeId() || null : null,
      lines,
    };

    if (this.isNew()) {
      this.purchasingService.createExpense(this.organizationId, request).subscribe({
        next: (result) => {
          this.saving.set(false);
          this.router.navigate(['/organizations', this.organizationId, 'purchasing', 'expenses', result.id]);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save expense. Please try again.');
        },
      });
    } else {
      this.purchasingService.updateExpense(this.organizationId, this.routeExpenseId, request).subscribe({
        next: () => {
          this.saving.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save expense. Please try again.');
        },
      });
    }
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.purchasingService.approveExpense(this.organizationId, this.routeExpenseId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve expense. Please try again.');
      },
    });
  }

  protected vatPercent(vatRate: VatRate): number {
    return vatRate === 'ThirteenPercentVat' ? 0.13 : 0;
  }

  private toLineInputs(): ExpenseLineInput[] | null {
    const lines = this.lines()
      .filter((l) => l.accountId && l.amount > 0)
      .map((l) => ({ accountId: l.accountId, amount: l.amount, vatRate: l.vatRate }));

    if (lines.length === 0) {
      this.errorMessage.set('Add at least one line with an Account and an Amount.');
      return null;
    }

    return lines;
  }

  private updateLine(key: number, patch: Partial<Omit<EditableLine, 'key'>>): void {
    this.lines.update((lines) => lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  }

  private newLine(): EditableLine {
    return { key: nextLineKey++, accountId: '', amount: 0, vatRate: 'NoVat' };
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private round(value: number): number {
    return Math.round(value * 100) / 100;
  }

  private load(): void {
    this.loading.set(true);
    this.purchasingService.getExpense(this.organizationId, this.routeExpenseId).subscribe({
      next: (expense) => {
        this.expense.set(expense);
        this.contactId.set(expense.contactId);
        this.date.set(expense.date);
        this.dueDate.set(expense.dueDate ?? '');
        this.supplierInvoiceReference.set(expense.supplierInvoiceReference ?? '');
        this.notes.set(expense.notes ?? '');
        this.tdsApplicable.set(expense.tdsApplicable);
        this.tdsTypeId.set(expense.tdsTypeId ?? '');
        this.lines.set(
          expense.lines.length > 0
            ? expense.lines.map((l) => ({ key: nextLineKey++, accountId: l.accountId, amount: l.amount, vatRate: l.vatRate }))
            : [this.newLine()],
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load expense.');
      },
    });
  }
}
