import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account, JournalVoucherDetail, JournalVoucherLineInput } from '../../../core/accounting/accounting.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { PrintingService } from '../../../core/printing/printing.service';
import { openBlankTabForPrint, openBlobInNewTab } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { DocumentTabs } from '../../../shared/document-tabs/document-tabs';
import { ReportingTagsEditor } from '../../../shared/reporting-tags/reporting-tags-editor';
import { CustomFieldsEditor } from '../../../shared/custom-fields/custom-fields-editor';
import { commitCustomFieldsThen } from '../../../shared/custom-fields/commit-custom-fields';

interface EditableLine {
  key: number;
  accountId: string;
  debit: number;
  credit: number;
  contactId: string | null;
}

let nextLineKey = 1;

/**
 * First "transactional document" chrome in this codebase -- NOT a record-detail-page clone
 * (contact-detail-page/product-detail-page's left-mini-profile-panel shape doesn't fit a
 * multi-line editable table). Establishes the template every later Sales/Purchase document
 * screen (Invoice, PurchaseBill, Payment, ...) clones: a live client-side-computed
 * Debit/Credit/Difference total (no round-trip needed -- PreviewGlPostingQuery exists server-side
 * mainly to prove the shared-rule contract, see AccountingService.previewGlPosting), an explicit
 * two-step Draft-save vs Approve action (not a single combined Create button), and a read-only
 * "GL Transactions" section once Approved.
 *
 * Pure signal-driven line-table state (no FormArray -- this codebase has no FormArray precedent
 * yet and a plain signal<EditableLine[]> is simpler for a client-driven "resubmit the whole
 * table" edit). Same route.paramMap subscription (not route.snapshot) as contact-detail-page,
 * for the same route-reuse reason (this route also serves both 'new' and ':journalVoucherId').
 */
@Component({
  selector: 'app-journal-voucher-detail-page',
  imports: [RouterLink, DatePipe, AmountPipe, BsDateInput, DocumentTabs, ReportingTagsEditor, CustomFieldsEditor],
  templateUrl: './journal-voucher-detail-page.html',
})
export class JournalVoucherDetailPage {
  /** Phase 27a: custom field values ride the document's own Save. See
   * commitCustomFieldsThen for why the commit is an rxjs operator rather than a
   * nested subscribe, and why a failed commit does not report the save as failed. */
  private readonly customFieldsEditor = viewChild(CustomFieldsEditor);

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly accountingService = inject(AccountingService);
  private readonly contactsService = inject(ContactsService);
  private readonly printingService = inject(PrintingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly printing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly journalVoucher = signal<JournalVoucherDetail | null>(null);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly contacts = signal<Contact[]>([]);
  protected readonly isNew = signal(false);

  protected readonly date = signal(this.today());
  protected readonly reference = signal('');
  protected readonly lines = signal<EditableLine[]>([]);

  protected routeJournalVoucherId = '';

  protected readonly totalDebit = computed(() => this.round(this.lines().reduce((sum, l) => sum + (l.debit || 0), 0)));
  protected readonly totalCredit = computed(() => this.round(this.lines().reduce((sum, l) => sum + (l.credit || 0), 0)));
  protected readonly difference = computed(() => this.round(this.totalDebit() - this.totalCredit()));

  protected readonly isDraft = computed(() => {
    const journalVoucher = this.journalVoucher();
    return this.isNew() || !journalVoucher || journalVoucher.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    return (
      !this.isNew() &&
      lines.length >= 2 &&
      this.difference() === 0 &&
      lines.every((l) => l.accountId && (l.debit > 0 || l.credit > 0))
    );
  });

  protected readonly sortedAccounts = computed(() => [...this.accounts()].sort((a, b) => a.code.localeCompare(b.code)));
  protected readonly sortedContacts = computed(() => [...this.contacts()].sort((a, b) => a.name.localeCompare(b.name)));

  constructor() {
    this.accountingService.listAllAccounts(this.organizationId).subscribe({ next: (accounts) => this.accounts.set(accounts) });
    this.contactsService.listAllContacts(this.organizationId).subscribe({ next: (contacts) => this.contacts.set(contacts) });

    this.route.paramMap.subscribe((params) => {
      this.routeJournalVoucherId = params.get('journalVoucherId')!;
      const isNew = this.routeJournalVoucherId === 'new';
      this.isNew.set(isNew);
      this.journalVoucher.set(null);
      this.errorMessage.set(null);

      if (isNew) {
        this.loading.set(false);
        this.date.set(this.today());
        this.reference.set('');
        this.lines.set([this.newLine(), this.newLine()]);
      } else {
        this.load();
      }
    });
  }

  protected accountLabel(accountId: string): string {
    const account = this.accounts().find((a) => a.id === accountId);
    return account ? `${account.code} — ${account.name}` : '—';
  }

  protected contactLabel(contactId: string | null): string {
    if (!contactId) {
      return '—';
    }
    const contact = this.contacts().find((c) => c.id === contactId);
    return contact ? contact.name : '—';
  }

  protected onAccountChange(key: number, event: Event): void {
    const accountId = (event.target as HTMLSelectElement).value;
    this.updateLine(key, { accountId });
  }

  protected onDebitChange(key: number, event: Event): void {
    const debit = (event.target as HTMLInputElement).valueAsNumber;
    this.updateLine(key, { debit: Number.isFinite(debit) ? debit : 0 });
  }

  protected onCreditChange(key: number, event: Event): void {
    const credit = (event.target as HTMLInputElement).valueAsNumber;
    this.updateLine(key, { credit: Number.isFinite(credit) ? credit : 0 });
  }

  protected onContactChange(key: number, event: Event): void {
    const contactId = (event.target as HTMLSelectElement).value || null;
    this.updateLine(key, { contactId });
  }

  protected addLine(): void {
    this.lines.update((lines) => [...lines, this.newLine()]);
  }

  protected removeLine(key: number): void {
    this.lines.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected saveDraft(): void {
    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const request = { date: this.date(), reference: this.reference() || null, lines };
    const request$ = this.isNew()
      ? this.accountingService.createJournalVoucher(this.organizationId, request)
      : this.accountingService.updateJournalVoucher(this.organizationId, this.routeJournalVoucherId, request);

    request$
      .pipe(commitCustomFieldsThen(this.customFieldsEditor(), (r) => r.id, (m) => this.errorMessage.set(m)))
      .subscribe({
      next: (result) => {
        this.saving.set(false);
        if (this.isNew()) {
          this.router.navigate(['/organizations', this.organizationId, 'accounting', 'journal-vouchers', result.id]);
        } else {
          this.load();
        }
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save journal voucher. Please try again.');
      },
    });
  }

  protected print(): void {
    this.printing.set(true);
    this.errorMessage.set(null);
    const tab = openBlankTabForPrint();

    this.printingService.printDocument(this.organizationId, 'JournalVoucher', this.routeJournalVoucherId).subscribe({
      next: (blob) => {
        this.printing.set(false);
        openBlobInNewTab(blob, tab);
      },
      error: (err: unknown) => {
        this.printing.set(false);
        tab?.close();
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print journal voucher. Please try again.');
      },
    });
  }

  protected voidJournalVoucher(): void {
    if (!window.confirm('Void this journal voucher? This reverses its GL posting and cannot be undone.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.accountingService.voidJournalVoucher(this.organizationId, this.routeJournalVoucherId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void journal voucher. Please try again.');
      },
    });
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.accountingService.approveJournalVoucher(this.organizationId, this.routeJournalVoucherId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve journal voucher. Please try again.');
      },
    });
  }

  private toLineInputs(): JournalVoucherLineInput[] | null {
    const lines = this.lines()
      .filter((l) => l.accountId && (l.debit > 0 || l.credit > 0))
      .map((l) => ({ accountId: l.accountId, debit: l.debit || 0, credit: l.credit || 0, contactId: l.contactId }));

    if (lines.length === 0) {
      this.errorMessage.set('Add at least one line with an Account and a Debit or Credit amount.');
      return null;
    }

    return lines;
  }

  private updateLine(key: number, patch: Partial<Pick<EditableLine, 'accountId' | 'debit' | 'credit' | 'contactId'>>): void {
    this.lines.update((lines) =>
      lines.map((l) => {
        if (l.key !== key) {
          return l;
        }
        const updated = { ...l, ...patch };
        // Debit and Credit are mutually exclusive per line (architecture-spec.md §3.4's
        // "exactly one of Debit/Credit" line invariant, enforced client-side too).
        if (patch.debit !== undefined && patch.debit > 0) {
          updated.credit = 0;
        }
        if (patch.credit !== undefined && patch.credit > 0) {
          updated.debit = 0;
        }
        return updated;
      }),
    );
  }

  private newLine(): EditableLine {
    return { key: nextLineKey++, accountId: '', debit: 0, credit: 0, contactId: null };
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private round(value: number): number {
    return Math.round(value * 100) / 100;
  }

  private load(): void {
    this.loading.set(true);
    this.accountingService.getJournalVoucher(this.organizationId, this.routeJournalVoucherId).subscribe({
      next: (journalVoucher) => {
        this.journalVoucher.set(journalVoucher);
        this.date.set(journalVoucher.date);
        this.reference.set(journalVoucher.reference ?? '');
        this.lines.set(
          journalVoucher.lines.length > 0
            ? journalVoucher.lines.map((l) => ({
                key: nextLineKey++,
                accountId: l.accountId,
                debit: l.debit,
                credit: l.credit,
                contactId: l.contactId,
              }))
            : [this.newLine(), this.newLine()],
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load journal voucher.');
      },
    });
  }
}
