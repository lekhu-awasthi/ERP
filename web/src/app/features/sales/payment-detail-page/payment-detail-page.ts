import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PaymentsService } from '../../../core/payments/payments.service';
import { GlLinePreviewDto, PaymentAllocationInput, PaymentDetail } from '../../../core/payments/payments.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { PaymentMode } from '../../../core/configuration/configuration.models';
import { SalesService } from '../../../core/sales/sales.service';
import { Invoice } from '../../../core/sales/sales.models';

interface EditableAllocation {
  key: number;
  targetDocumentId: string;
  amount: number;
}

let nextAllocationKey = 1;

/** Clones journal-voucher-detail-page's chrome once more -- an Allocations table (against
 * Approved invoices) instead of a Debit/Credit line table, a "Suggest (FIFO)" button backed by
 * GetDefaultPaymentAllocationsQuery, and the same live GL-preview-before-approve behavior
 * confirmed in erp-module-scan.md's hands-on pass. */
@Component({
  selector: 'app-payment-detail-page',
  imports: [RouterLink, DatePipe],
  templateUrl: './payment-detail-page.html',
})
export class PaymentDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly paymentsService = inject(PaymentsService);
  private readonly contactsService = inject(ContactsService);
  private readonly accountingService = inject(AccountingService);
  private readonly configurationService = inject(ConfigurationService);
  private readonly salesService = inject(SalesService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly suggesting = signal(false);
  protected readonly previewingGl = signal(false);
  protected readonly glPreview = signal<GlLinePreviewDto[] | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly payment = signal<PaymentDetail | null>(null);
  protected readonly customers = signal<Contact[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly paymentModes = signal<PaymentMode[]>([]);
  protected readonly approvedInvoices = signal<Invoice[]>([]);
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly date = signal(this.today());
  protected readonly paymentModeId = signal('');
  protected readonly accountId = signal('');
  protected readonly amount = signal(0);
  protected readonly reference = signal('');
  protected readonly allocations = signal<EditableAllocation[]>([]);

  private routePaymentId = '';

  protected readonly sortedAccounts = computed(() => [...this.accounts()].sort((a, b) => a.code.localeCompare(b.code)));

  protected readonly customerInvoices = computed(() =>
    this.approvedInvoices().filter((i) => i.contactId === this.contactId()),
  );

  protected readonly allocatedTotal = computed(() => this.round(this.allocations().reduce((sum, a) => sum + (a.amount || 0), 0)));
  protected readonly remaining = computed(() => this.round(this.amount() - this.allocatedTotal()));

  protected readonly isDraft = computed(() => {
    const payment = this.payment();
    return this.isNew() || !payment || payment.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    return (
      !this.isNew() &&
      this.allocations().length > 0 &&
      this.allocations().every((a) => a.targetDocumentId && a.amount > 0) &&
      this.remaining() === 0
    );
  });

  constructor() {
    this.contactsService.listContacts(this.organizationId, 'Customer').subscribe({ next: (c) => this.customers.set(c) });
    this.accountingService.listAccounts(this.organizationId).subscribe({ next: (a) => this.accounts.set(a) });
    this.configurationService.listPaymentModes(this.organizationId).subscribe({ next: (m) => this.paymentModes.set(m) });
    this.salesService.listInvoices(this.organizationId, 'Approved').subscribe({ next: (i) => this.approvedInvoices.set(i) });

    this.route.paramMap.subscribe((params) => {
      this.routePaymentId = params.get('paymentId')!;
      const isNew = this.routePaymentId === 'new';
      this.isNew.set(isNew);
      this.payment.set(null);
      this.errorMessage.set(null);

      if (isNew) {
        this.loading.set(false);
        this.contactId.set('');
        this.date.set(this.today());
        this.paymentModeId.set('');
        this.accountId.set('');
        this.amount.set(0);
        this.reference.set('');
        this.allocations.set([]);
      } else {
        this.load();
      }
    });
  }

  protected contactLabel(contactId: string): string {
    const contact = this.customers().find((c) => c.id === contactId);
    return contact ? `${contact.code} — ${contact.name}` : '—';
  }

  protected accountLabel(accountId: string): string {
    const account = this.accounts().find((a) => a.id === accountId);
    return account ? `${account.code} — ${account.name}` : '—';
  }

  protected invoiceLabel(invoiceId: string): string {
    const invoice = this.approvedInvoices().find((i) => i.id === invoiceId);
    return invoice ? invoice.code : '—';
  }

  protected onAllocationInvoiceChange(key: number, event: Event): void {
    const targetDocumentId = (event.target as HTMLSelectElement).value;
    this.updateAllocation(key, { targetDocumentId });
  }

  protected onAllocationAmountChange(key: number, event: Event): void {
    const amount = (event.target as HTMLInputElement).valueAsNumber;
    this.updateAllocation(key, { amount: Number.isFinite(amount) ? amount : 0 });
  }

  protected addAllocation(): void {
    this.allocations.update((rows) => [...rows, { key: nextAllocationKey++, targetDocumentId: '', amount: 0 }]);
  }

  protected removeAllocation(key: number): void {
    this.allocations.update((rows) => rows.filter((r) => r.key !== key));
  }

  protected suggestAllocations(): void {
    if (!this.contactId() || this.amount() <= 0) {
      this.errorMessage.set('Select a Customer and enter an Amount first.');
      return;
    }

    this.suggesting.set(true);
    this.errorMessage.set(null);

    this.paymentsService.getDefaultAllocations(this.organizationId, this.contactId(), this.amount()).subscribe({
      next: (suggestions) => {
        this.suggesting.set(false);
        this.allocations.set(
          suggestions.map((s) => ({ key: nextAllocationKey++, targetDocumentId: s.targetDocumentId, amount: s.amount })),
        );
      },
      error: (err: unknown) => {
        this.suggesting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not compute default allocations.');
      },
    });
  }

  protected previewGlPosting(): void {
    if (!this.accountId() || this.amount() <= 0) {
      this.errorMessage.set('Select a cash/bank Account and enter an Amount first.');
      return;
    }

    this.previewingGl.set(true);
    this.errorMessage.set(null);

    this.paymentsService.previewPaymentGlPosting(this.organizationId, this.accountId(), this.amount()).subscribe({
      next: (lines) => {
        this.previewingGl.set(false);
        this.glPreview.set(lines);
      },
      error: (err: unknown) => {
        this.previewingGl.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not preview GL posting.');
      },
    });
  }

  protected saveDraft(): void {
    if (!this.contactId()) {
      this.errorMessage.set('Select a Customer.');
      return;
    }
    if (!this.accountId()) {
      this.errorMessage.set('Select a cash/bank Account.');
      return;
    }
    if (this.amount() <= 0) {
      this.errorMessage.set('Amount must be greater than zero.');
      return;
    }

    const allocations: PaymentAllocationInput[] = this.allocations()
      .filter((a) => a.targetDocumentId && a.amount > 0)
      .map((a) => ({ targetDocumentType: 'Invoice', targetDocumentId: a.targetDocumentId, amount: a.amount }));

    this.saving.set(true);
    this.errorMessage.set(null);

    const request = {
      contactId: this.contactId(),
      date: this.date(),
      paymentModeId: this.paymentModeId() || null,
      accountId: this.accountId(),
      amount: this.amount(),
      reference: this.reference() || null,
      allocations,
    };

    if (this.isNew()) {
      this.paymentsService.createPayment(this.organizationId, request).subscribe({
        next: (result) => {
          this.saving.set(false);
          this.router.navigate(['/organizations', this.organizationId, 'payments', result.id]);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save payment. Please try again.');
        },
      });
    } else {
      this.paymentsService.updatePayment(this.organizationId, this.routePaymentId, request).subscribe({
        next: () => {
          this.saving.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save payment. Please try again.');
        },
      });
    }
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.paymentsService.approvePayment(this.organizationId, this.routePaymentId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve payment. Please try again.');
      },
    });
  }

  private updateAllocation(key: number, patch: Partial<Omit<EditableAllocation, 'key'>>): void {
    this.allocations.update((rows) => rows.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private round(value: number): number {
    return Math.round(value * 100) / 100;
  }

  private load(): void {
    this.loading.set(true);
    this.paymentsService.getPayment(this.organizationId, this.routePaymentId).subscribe({
      next: (payment) => {
        this.payment.set(payment);
        this.contactId.set(payment.contactId);
        this.date.set(payment.date);
        this.paymentModeId.set(payment.paymentModeId ?? '');
        this.accountId.set(payment.accountId);
        this.amount.set(payment.amount);
        this.reference.set(payment.reference ?? '');
        this.allocations.set(
          payment.allocations.map((a) => ({ key: nextAllocationKey++, targetDocumentId: a.targetDocumentId, amount: a.amount })),
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load payment.');
      },
    });
  }
}
