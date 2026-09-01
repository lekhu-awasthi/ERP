import { Component, computed, inject, signal } from '@angular/core';
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
import { CreatePaymentResult, PaymentDirection } from '../../../core/payments/payments.models';
import { InboxPrefill } from '../../../core/workflow/inbox.models';
import { InboxService } from '../../../core/workflow/inbox.service';
import { InboxConversionPanel } from '../../../shared/source-document/inbox-conversion-panel';

/**
 * Phase 17 -- Quick Payment/Quick Receipt (docs/phase-17-status.md decision #7): a thin variant of
 * the existing Payment aggregate (CreatePaymentCommand/ApprovePaymentCommand with
 * Allocations = []), not a port of Tigg's own generic multi-line-Accounts document (that shape
 * doesn't fit this codebase's Contact/Account separation -- see decision #7's full reasoning). One
 * component parameterized by route data `direction` serves both screens.
 *
 * <p><b>Phase 22 changed this from one-shot to Draft-then-Approve</b>, so it matches every other
 * document type in the product. It used to create and approve in a single click, which posted to the
 * General Ledger with no review step -- tolerable for a screen someone types by hand, wrong once the
 * Document inbox can pre-fill it from a scan a machine read. Saving now leaves a Draft that lands in
 * the Transaction Approval queue like any other, so a second person can approve it there; Approve is
 * also offered here for the single-operator case.</p>
 *
 * <p>The two steps stay <b>on this page</b> rather than navigating to `payment-detail-page`, because
 * that page's own `canApprove()` requires `allocations.length > 0 && remaining === 0` -- a
 * zero-allocation Quick Payment sent there would be a Draft whose Approve button is permanently
 * disabled. That gate is exactly why Phase 17's decision #7 gave this screen its own component in
 * the first place.</p>
 */
@Component({
  selector: 'app-quick-payment-page',
  imports: [ReactiveFormsModule, RouterLink, InboxConversionPanel],
  templateUrl: './quick-payment-page.html',
})
export class QuickPaymentPage {
  private readonly route = inject(ActivatedRoute);
  private readonly paymentsService = inject(PaymentsService);
  private readonly accountingService = inject(AccountingService);
  private readonly configurationService = inject(ConfigurationService);
  private readonly contactsService = inject(ContactsService);
  private readonly fb = inject(FormBuilder);
  private readonly inboxService = inject(InboxService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly direction = (this.route.snapshot.data['direction'] as PaymentDirection) ?? 'Received';
  protected readonly isReceipt = this.direction === 'Received';

  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  /** The Draft this page just created, or null while the form is still being filled in. Its
   * presence is what switches the page from "form" to "saved draft", and it is the only thing
   * `approve()` acts on -- never the form, which is disabled by then. */
  protected readonly draft = signal<CreatePaymentResult | null>(null);
  protected readonly isDraftSaved = computed(() => this.draft() !== null);
  protected readonly contacts = signal<Contact[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly paymentModes = signal<PaymentMode[]>([]);

  /** Phase 22 -- set when opened from the Document inbox's "+ Add as" with ?inboxDocumentId=. */
  protected readonly inboxPrefill = signal<InboxPrefill | null>(null);
  private inboxDocumentId: string | null = null;

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

    // This page has no :id route, so route reuse across a create/edit boundary (the Phase 3 gotcha)
    // does not apply and a snapshot read is safe here.
    this.inboxDocumentId = this.route.snapshot.queryParamMap.get('inboxDocumentId');
    if (this.inboxDocumentId) {
      this.loadInboxPrefill(this.inboxDocumentId);
    }
  }

  private loadInboxPrefill(inboxDocumentId: string): void {
    this.inboxService.getPrefill(this.organizationId, inboxDocumentId, 'Payment').subscribe({
      next: (prefill) => {
        this.inboxPrefill.set(prefill);

        // patchValue, not a computed() over the FormGroup: this is the Reactive Forms page where
        // CLAUDE.md's zoneless-computed-over-FormControl.value gotcha was found, and a plain
        // FormControl write is exactly what that lesson says to use.
        this.form.patchValue({
          ...(prefill.contactId ? { contactId: prefill.contactId } : {}),
          ...(prefill.date ? { date: prefill.date } : {}),
          ...(prefill.reference ? { reference: prefill.reference } : {}),
          ...(prefill.totalAmount !== null ? { amount: prefill.totalAmount } : {}),
        });
      },
      error: (err: unknown) => {
        this.inboxDocumentId = null;
        this.errorMessage.set(
          extractErrorMessage(err) ?? 'Could not load the suggested values from the inbox document.',
        );
      },
    });
  }

  /**
   * Links on the *created* payment id, before approval is even attempted -- the record the scan is
   * evidence for exists at that point, and a failed approval leaves a real Draft Payment the user
   * still wants the scan attached to.
   */
  private linkInboxDocument(paymentId: string): void {
    const inboxDocumentId = this.inboxDocumentId;
    if (!inboxDocumentId) {
      return;
    }

    this.inboxDocumentId = null;
    this.inboxService.linkDocument(this.organizationId, inboxDocumentId, 'Payment', paymentId).subscribe({
      next: () => this.inboxPrefill.set(null),
      error: (err: unknown) =>
        this.errorMessage.set(
          extractErrorMessage(err) ?? 'The payment was saved, but it could not be linked back to the inbox document.',
        ),
    });
  }

  protected requiresChequeDetails(): boolean {
    return this.paymentModes().find((m) => m.id === this.selectedPaymentModeId())?.requiresChequeDetails ?? false;
  }

  protected onPaymentModeChange(id: string): void {
    this.selectedPaymentModeId.set(id);
    this.form.controls.paymentModeId.setValue(id);
  }

  /**
   * Step one: create the Draft. Deliberately does **not** approve -- see this class's own doc
   * comment. The inbox link is established here, on the created id, because the record the scan is
   * evidence for exists at this point whether or not anyone ever approves it.
   */
  protected saveDraft(): void {
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
          this.saving.set(false);
          this.draft.set(created);
          // Nothing further may edit this payment from here, so the form is frozen rather than left
          // looking editable while the Draft it produced drifts out of sync with it.
          this.form.disable();
          this.linkInboxDocument(created.id);
          this.successMessage.set(
            `${this.documentLabel()} saved as a draft. Approve it below, or leave it for someone else to approve from the Transaction Approval queue.`,
          );
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save the draft. Please try again.');
        },
      });
  }

  /**
   * Step two: post it. Reads the id off the saved Draft, never off the form -- the form is disabled
   * by now, and the Draft is the only thing that exists in the database.
   */
  protected approve(): void {
    const draft = this.draft();
    if (!draft) {
      return;
    }

    this.approving.set(true);
    this.errorMessage.set(null);

    this.paymentsService.approvePayment(this.organizationId, draft.id).subscribe({
      next: (approved) => {
        this.approving.set(false);
        // The real code is read off the *approve* response, not the create one -- numbering happens
        // at Approve, so `created.code` is still "DRAFT" (phase-17-status.md's bug #3).
        this.draft.set({ id: approved.id, code: approved.code, status: approved.status });
        this.successMessage.set(`${this.documentLabel()} ${approved.code} approved.`);
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(
          extractErrorMessage(err) ??
            'Could not approve. The draft is saved -- you can approve it from the Transaction Approval queue.',
        );
      },
    });
  }

  /** Clears the page for the next one. The saved payment is untouched and reachable from the
   * Payments list either way. */
  protected startAnother(): void {
    this.draft.set(null);
    this.successMessage.set(null);
    this.errorMessage.set(null);
    this.selectedPaymentModeId.set('');
    this.form.enable();
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
  }

  protected documentLabel(): string {
    return this.isReceipt ? 'Quick Receipt' : 'Quick Payment';
  }

  /** Where the saved payment lives once this page is done with it. Direction picks the route, the
   * same split the approval queue and the audit report already use. */
  protected savedPaymentRoute(): string[] | null {
    const draft = this.draft();
    if (!draft) {
      return null;
    }

    return this.isReceipt
      ? ['/organizations', this.organizationId, 'payments', draft.id]
      : ['/organizations', this.organizationId, 'purchasing', 'supplier-payments', draft.id];
  }
}
