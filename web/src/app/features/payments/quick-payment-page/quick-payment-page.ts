import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { PaymentMode } from '../../../core/configuration/configuration.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { PaymentsService } from '../../../core/payments/payments.service';
import { PaymentDirection } from '../../../core/payments/payments.models';

/**
 * Phase 17 -- Quick Payment/Quick Receipt (docs/phase-17-status.md decision #7): a thin variant of
 * the existing Payment aggregate (CreatePaymentCommand/ApprovePaymentCommand with
 * Allocations = []), not a port of Tigg's own generic multi-line-Accounts document (that shape
 * doesn't fit this codebase's Contact/Account separation -- see decision #7's full reasoning). One
 * component parameterized by route data `direction` serves both screens.
 */
@Component({
  selector: 'app-quick-payment-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './quick-payment-page.html',
})
export class QuickPaymentPage {
  private readonly route = inject(ActivatedRoute);
  private readonly paymentsService = inject(PaymentsService);
  private readonly accountingService = inject(AccountingService);
  private readonly configurationService = inject(ConfigurationService);
  private readonly contactsService = inject(ContactsService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly direction = (this.route.snapshot.data['direction'] as PaymentDirection) ?? 'Received';
  protected readonly isReceipt = this.direction === 'Received';

  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly contacts = signal<Contact[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly paymentModes = signal<PaymentMode[]>([]);

  protected readonly form = this.fb.nonNullable.group({
    contactId: ['', Validators.required],
    date: [new Date().toISOString().slice(0, 10), Validators.required],
    paymentModeId: [''],
    accountId: ['', Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    reference: [''],
    chequeNo: [''],
    chequeDate: [new Date().toISOString().slice(0, 10)],
    receivedDate: [new Date().toISOString().slice(0, 10)],
  });

  /** Plain signal (not a computed() over the FormGroup's own .value) -- FormControl.value is a
   * plain property, not a signal, so a computed() reading it never re-evaluates on change in this
   * zoneless app (confirmed live: the Cheque Details section silently never appeared). The Payment
   * Mode <select>'s (change) handler below updates this signal directly instead. */
  protected readonly selectedPaymentModeId = signal('');

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, this.isReceipt ? 'Customer' : 'Supplier').subscribe({
      next: (c) => this.contacts.set(c),
    });
    this.accountingService.listAllAccounts(this.organizationId).subscribe({
      next: (a) => this.accounts.set(a.filter((x) => x.kind === 'Bank' || x.kind === 'Cash')),
    });
    this.configurationService.listPaymentModes(this.organizationId).subscribe({ next: (m) => this.paymentModes.set(m) });
  }

  protected requiresChequeDetails(): boolean {
    return this.paymentModes().find((m) => m.id === this.selectedPaymentModeId())?.requiresChequeDetails ?? false;
  }

  protected onPaymentModeChange(id: string): void {
    this.selectedPaymentModeId.set(id);
    this.form.controls.paymentModeId.setValue(id);
  }

  protected save(): void {
    if (this.form.invalid || (this.requiresChequeDetails() && !this.form.controls.chequeNo.value)) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const { contactId, date, paymentModeId, accountId, amount, reference, chequeNo, chequeDate, receivedDate } =
      this.form.getRawValue();

    this.paymentsService
      .createPayment(this.organizationId, {
        contactId,
        direction: this.direction,
        date,
        paymentModeId: paymentModeId || null,
        accountId,
        amount,
        reference: reference || null,
        allocations: [],
        chequeDetails: this.requiresChequeDetails() ? { chequeNo, chequeDate, receivedDate: receivedDate || null } : null,
      })
      .subscribe({
        next: (created) => {
          this.paymentsService.approvePayment(this.organizationId, created.id).subscribe({
            next: (approved) => {
              this.saving.set(false);
              this.successMessage.set(`${this.isReceipt ? 'Quick Receipt' : 'Quick Payment'} ${approved.code} approved.`);
              this.selectedPaymentModeId.set('');
              this.form.reset({
                contactId: '',
                date: new Date().toISOString().slice(0, 10),
                paymentModeId: '',
                accountId: '',
                amount: 0,
                reference: '',
                chequeNo: '',
                chequeDate: new Date().toISOString().slice(0, 10),
                receivedDate: new Date().toISOString().slice(0, 10),
              });
            },
            error: (err: unknown) => {
              this.saving.set(false);
              this.errorMessage.set(extractErrorMessage(err) ?? 'Created but could not approve. See Payments list.');
            },
          });
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not create payment. Please try again.');
        },
      });
  }
}
