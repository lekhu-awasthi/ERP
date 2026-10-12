import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { BASE_CURRENCY_CODE } from '../../../core/organizations/organizations.models';
import { CurrencyRateFields } from '../../../shared/currency/currency-rate-fields';

import { extractErrorMessage, extractWarningKind } from '../../../core/auth/api-error';
import { PaymentsService } from '../../../core/payments/payments.service';
import { GlLinePreviewDto, OpenAllocationTarget, PaymentAllocationInput, PaymentDetail } from '../../../core/payments/payments.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { PaymentMode } from '../../../core/configuration/configuration.models';
import { SourceDocumentPanel } from '../../../shared/source-document/source-document-panel';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { DocumentTabs } from '../../../shared/document-tabs/document-tabs';
import { ReportingTagsEditor } from '../../../shared/reporting-tags/reporting-tags-editor';
import { CustomFieldsEditor } from '../../../shared/custom-fields/custom-fields-editor';
import { commitCustomFieldsThen } from '../../../shared/custom-fields/commit-custom-fields';
import { PrintingService } from '../../../core/printing/printing.service';
import { openBlankTabForPrint, openBlobInNewTab } from '../../../shared/download-file';
import { SendEmailDialog } from '../../../shared/send-email/send-email-dialog';
import { DocumentLocationPicker } from '../../../shared/locations/document-location-picker';
import { FieldError, FieldErrorMessage } from '../../../shared/a11y/field-error';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { nepalToday } from '../../../shared/formatting/nepal-time';

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
  imports: [RouterLink, SourceDocumentPanel, AmountPipe, BsDateInput, DocumentTabs, ReportingTagsEditor, CustomFieldsEditor, CurrencyRateFields, SendEmailDialog, DocumentLocationPicker, StatusBanner, FieldErrorMessage, NepaliDatePipe],
  templateUrl: './payment-detail-page.html',
})
export class PaymentDetailPage {
  /** Phase 27a: custom field values ride the document's own Save. See
   * commitCustomFieldsThen for why the commit is an rxjs operator rather than a
   * nested subscribe, and why a failed commit does not report the save as failed. */
  private readonly customFieldsEditor = viewChild(CustomFieldsEditor);

  private readonly route = inject(ActivatedRoute);
  private readonly printingService = inject(PrintingService);
  private readonly router = inject(Router);
  private readonly paymentsService = inject(PaymentsService);
  private readonly contactsService = inject(ContactsService);
  private readonly accountingService = inject(AccountingService);
  private readonly configurationService = inject(ConfigurationService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency, owned here
  // and rendered by the shared app-currency-rate-fields control.
  protected readonly currencyCode = signal(BASE_CURRENCY_CODE);
  protected readonly exchangeRate = signal(1);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly suggesting = signal(false);
  protected readonly previewingGl = signal(false);
  protected readonly glPreview = signal<GlLinePreviewDto[] | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  /**
   * Phase 47 -- the form's per-field error state. Takes this page's own `errorMessage` signal rather
   * than owning a second one, so the banner and the field marking cannot disagree (see `FieldError`).
   */
  protected readonly fieldError = new FieldError(this.errorMessage);
  protected readonly payment = signal<PaymentDetail | null>(null);
  protected readonly customers = signal<Contact[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly paymentModes = signal<PaymentMode[]>([]);
  /**
   * Phase 88 -- this customer's invoices with something left to settle, in this payment's currency, oldest due first
   * (`ListOpenAllocationTargetsQuery`). Until now the picker was the first 200 approved invoices of the whole
   * organization filtered here, which offered settled invoices and lost every unpaid one past the 200th.
   */
  protected readonly openTargets = signal<OpenAllocationTarget[]>([]);
  private targetsRequest = 0;
  protected readonly isNew = signal(false);

  protected readonly contactId = signal('');
  protected readonly date = signal(this.today());
  protected readonly paymentModeId = signal('');
  protected readonly accountId = signal('');
  protected readonly amount = signal(0);
  protected readonly reference = signal('');

  /**
   * Phase 35a (FR-2.3/FR-3.3) -- the billing location this document is raised from, shown by the
   * header picker `app-document-location-picker` renders. Empty means "let the server pick the
   * default", which `LocationResolver` turns into the tenant's HeadOffice, or into nothing when
   * this document type is outside the tenant's `LocationScopeMode`.
   */
  protected readonly locationId = signal('');
  protected readonly allocations = signal<EditableAllocation[]>([]);

  protected readonly printing = signal(false);
  protected routePaymentId = '';

  protected readonly sortedAccounts = computed(() => [...this.accounts()].sort((a, b) => a.code.localeCompare(b.code)));

  /**
   * Phase 88 -- a target this draft already names that is no longer open (another payment settled it since) stays
   * selectable under its number, or the row would silently show nothing chosen.
   */
  protected readonly savedClosedTargets = computed(() => {
    const open = new Set(this.openTargets().map((t) => t.id));
    return (this.payment()?.allocations ?? []).filter((a) => !open.has(a.targetDocumentId));
  });

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
    this.contactsService.listAllContacts(this.organizationId, 'Customer').subscribe({ next: (c) => this.customers.set(c) });
    this.accountingService.listAllAccounts(this.organizationId).subscribe({ next: (a) => this.accounts.set(a) });
    this.configurationService.listPaymentModes(this.organizationId).subscribe({ next: (m) => this.paymentModes.set(m) });

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
        this.locationId.set('');
        this.currencyCode.set(BASE_CURRENCY_CODE);
        this.exchangeRate.set(1);
        this.allocations.set([]);
        this.refreshTargets();
      } else {
        this.load();
      }
    });

    // Quick-action prefill (FR-4.6, roadmap Phase 18 -- "Record Payment") -- subscribed after
    // paramMap above so a fresh 'new' navigation's form reset always runs before this applies the
    // ?contactId= query param, not after. Read reactively (not route.snapshot), per the
    // route-reuse gotcha.
    this.route.queryParamMap.subscribe((params) => {
      const contactId = params.get('contactId');
      if (contactId && this.isNew()) {
        this.contactId.set(contactId);
        this.refreshTargets();
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
    const saved = this.payment()?.allocations.find((a) => a.targetDocumentId === invoiceId);
    return saved?.targetCode ?? this.openTargets().find((t) => t.id === invoiceId)?.code ?? '—';
  }

  protected onContactChange(contactId: string): void {
    this.contactId.set(contactId);
    this.refreshTargets();
  }

  protected onCurrencyChange(currencyCode: string): void {
    this.currencyCode.set(currencyCode);
    this.refreshTargets();
  }

  /** Phase 88 -- reloads the picker for the current customer and currency; a reply to an older question is dropped. */
  private refreshTargets(): void {
    const request = ++this.targetsRequest;
    if (!this.contactId()) {
      this.openTargets.set([]);
      return;
    }

    this.paymentsService
      .listOpenAllocationTargets(this.organizationId, this.contactId(), 'Received', this.currencyCode())
      .subscribe({
        next: (targets) => {
          if (request === this.targetsRequest) {
            this.openTargets.set(targets);
          }
        },
        error: (err: unknown) => {
          if (request === this.targetsRequest) {
            this.openTargets.set([]);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the open invoices.');
          }
        },
      });
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

    this.paymentsService.getDefaultAllocations(this.organizationId, this.contactId(), this.amount(), 'Received', this.currencyCode()).subscribe({
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

    // Phase 88 -- the currency, the rate and the allocations too: Approve's entry depends on all three.
    this.paymentsService.previewPaymentGlPosting(this.organizationId, {
      accountId: this.accountId(),
      amount: this.amount(),
      direction: 'Received',
      currencyCode: this.currencyCode(),
      exchangeRate: this.exchangeRate(),
      allocations: this.allocationInputs(),
    }).subscribe({
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
      this.fieldError.fail('payment-detail-page-customer', 'Select a Customer.');
      return;
    }
    if (!this.accountId()) {
      this.fieldError.fail('payment-detail-page-deposit-to-account', 'Select a cash/bank Account.');
      return;
    }
    if (this.amount() <= 0) {
      this.fieldError.fail('payment-detail-page-amount', 'Amount must be greater than zero.');
      return;
    }

    const allocations = this.allocationInputs();

    this.saving.set(true);
    this.errorMessage.set(null);

    const request = {

      currencyCode: this.currencyCode(),

      exchangeRate: this.exchangeRate(),
      contactId: this.contactId(),
      direction: 'Received' as const,
      date: this.date(), locationId: this.locationId() || null,
      paymentModeId: this.paymentModeId() || null,
      accountId: this.accountId(),
      amount: this.amount(),
      reference: this.reference() || null,
      allocations,
    };

    if (this.isNew()) {
      this.paymentsService.createPayment(this.organizationId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), (r) => r.id, (m) => this.errorMessage.set(m)))
        .subscribe({
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
      this.paymentsService.updatePayment(this.organizationId, this.routePaymentId, request)
        .pipe(commitCustomFieldsThen(this.customFieldsEditor(), () => this.routePaymentId, (m) => this.errorMessage.set(m)))
        .subscribe({
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

  protected voidPayment(): void {
    if (!window.confirm('Void this payment? This releases its allocations and reverses its GL posting, and cannot be undone.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.paymentsService.voidPayment(this.organizationId, this.routePaymentId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void payment. Please try again.');
      },
    });
  }

  protected approve(overrideNegativeCashBalanceWarning = false): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.paymentsService.approvePayment(this.organizationId, this.routePaymentId, overrideNegativeCashBalanceWarning).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);

        // Phase 31 -- a 422 here is the confirmable Negative Cash Balance warning
        // (TenantSettings.NegativeCashBalanceAction = Warn), not a failure. Continue
        // resubmits with the acknowledgement; Cancel leaves the document in Draft.
        if (extractWarningKind(err) === 'NegativeCashBalance') {
          const message = extractErrorMessage(err) ?? 'This would leave a bank or cash account negative.';
          if (window.confirm(`${message}

Approve anyway?`)) {
            this.approve(true);
            return;
          }
          this.errorMessage.set(message);
          return;
        }

        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve payment. Please try again.');
      },
    });
  }

  private allocationInputs(): PaymentAllocationInput[] {
    return this.allocations()
      .filter((a) => a.targetDocumentId && a.amount > 0)
      .map((a) => ({ targetDocumentType: 'Invoice', targetDocumentId: a.targetDocumentId, amount: a.amount }));
  }

  private updateAllocation(key: number, patch: Partial<Omit<EditableAllocation, 'key'>>): void {
    this.allocations.update((rows) => rows.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  private today(): string {
    return nepalToday();
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
        this.locationId.set(payment.locationId ?? '');
        this.currencyCode.set(payment.currencyCode);
        this.exchangeRate.set(payment.exchangeRate);
        this.allocations.set(
          payment.allocations.map((a) => ({ key: nextAllocationKey++, targetDocumentId: a.targetDocumentId, amount: a.amount })),
        );
        this.loading.set(false);
        this.refreshTargets();
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load payment.');
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

    this.printingService.printDocument(this.organizationId, 'Payment', this.routePaymentId).subscribe({
      next: (blob) => {
        this.printing.set(false);
        openBlobInNewTab(blob, tab);
      },
      error: (err: unknown) => {
        this.printing.set(false);
        tab?.close();
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print receipt. Please try again.');
      },
    });
  }
}
