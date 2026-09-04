import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact, ContactStatementDto } from '../../../core/contacts/contacts.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { openBlankTabForPrint, openBlobInNewTab, triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Read-only report screen -- roadmap Phase 9's ContactStatementQuery (ContactType=Supplier).
 * Confirmed live directly against the reference product's own Supplier Statement screen during this
 * phase's design pass -- identical Txn Date/Txn Type/Txn No/Reference No/Debit/Credit/Balance columns
 * to Customer's confirmed shape, an Opening Balance row and a Closing Balance row, balances suffixed
 * "DR"/"CR". Debit/Credit follow real double-entry polarity -- AP is credit-normal for a Supplier,
 * the exact opposite of Customer Statement's AR-debit-normal polarity (confirmed directly against the
 * live screen: its Opening Balance row carried its value in the Credit column) -- computed
 * server-side, this page only formats already-authoritative numbers. Paginated (Phase 16c) -- see
 * customer-statement-page.ts's doc comment on OpeningBalance/ClosingBalance staying pagination-safe.
 */
@Component({
  selector: 'app-supplier-statement-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput, NepaliDatePipe],
  templateUrl: './supplier-statement-page.html',
})
export class SupplierStatementPage {
  private readonly route = inject(ActivatedRoute);
  private readonly contactsService = inject(ContactsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly statement = signal<ContactStatementDto | null>(null);
  protected readonly suppliers = signal<Contact[]>([]);

  protected readonly contactId = signal(this.route.snapshot.queryParamMap.get('contactId') ?? '');
  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);
  protected readonly confirming = signal(false);

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Supplier').subscribe({ next: (c) => this.suppliers.set(c) });

    // Pre-filled from the Contact Overview tab's "View Full Statement" link -- the sensible default
    // date range is this page's own existing first-of-month-to-today default, not a new convention.
    if (this.contactId()) {
      this.load();
    }
  }

  protected onContactChange(event: Event): void {
    this.contactId.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.page.set(1);
    this.load();
  }

  protected onToDateChange(value: string): void {
    this.toDate.set(value);
    this.page.set(1);
    this.load();
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.page.set(1);
    this.load();
  }

  protected exportCurrentView(): void {
    this.runExport(false, this.page(), this.pageSize());
  }

  protected exportFullDataset(): void {
    this.runExport(true, 1, this.pageSize());
  }

  /**
   * Phase 27b -- the balance-confirmation letter (FR-11.3), CustomTemplate's second real consumer.
   * It confirms the closing balance of exactly the period on screen, so it takes the To date rather
   * than asking for another one.
   */
  protected printConfirmation(): void {
    if (!this.contactId()) {
      return;
    }

    this.confirming.set(true);
    this.errorMessage.set(null);
    const tab = openBlankTabForPrint();

    this.contactsService
      .printBalanceConfirmation(this.organizationId, 'Supplier', this.contactId(), this.toDate())
      .subscribe({
        next: (blob) => {
          this.confirming.set(false);
          openBlobInNewTab(blob, tab);
        },
        error: (err: unknown) => {
          this.confirming.set(false);
          tab?.close();
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not build the balance confirmation.');
        },
      });
  }

  private runExport(full: boolean, page: number, pageSize: number): void {
    if (!this.contactId()) {
      return;
    }
    this.exporting.set(true);
    this.contactsService
      .exportSupplierStatement(this.organizationId, this.contactId(), this.fromDate(), this.toDate(), full, page, pageSize)
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `SupplierStatement_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Supplier Statement.');
        },
      });
  }

  private load(): void {
    if (!this.contactId()) {
      this.statement.set(null);
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);

    this.contactsService
      .getSupplierStatement(this.organizationId, this.contactId(), this.fromDate(), this.toDate(), this.page(), this.pageSize())
      .subscribe({
        next: (statement) => {
          this.statement.set(statement);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Supplier Statement.');
        },
      });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private firstOfMonth(): string {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
  }
}
