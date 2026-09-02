import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact, ContactStatementDto } from '../../../core/contacts/contacts.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Read-only report screen -- roadmap Phase 9's ContactStatementQuery (ContactType=Customer), the
 * real running-balance ledger architecture-spec.md §4.2 names and every prior Phase 8 report's
 * Opening/Closing Balance approximation deferred to. Confirmed live shape (architecture-spec.md
 * line 277): a full running-balance ledger, Opening Balance row + every transaction row. Single
 * Contact at a time -- see ContactStatementQuery's own doc comment on why this simplifies the live
 * screen's multi-select bulk-print convenience down to one-Account-at-a-time. Debit/Credit follow
 * real double-entry polarity (AR is debit-normal for a Customer), computed server-side along with
 * the DR/CR balance suffix -- this page only formats already-authoritative numbers. Paginated
 * (Phase 16c) -- OpeningBalance/ClosingBalance always reflect the full date range regardless of
 * which page of rows is displayed (computed server-side before pagination slices the row list).
 */
@Component({
  selector: 'app-customer-statement-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput, NepaliDatePipe],
  templateUrl: './customer-statement-page.html',
})
export class CustomerStatementPage {
  private readonly route = inject(ActivatedRoute);
  private readonly contactsService = inject(ContactsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly statement = signal<ContactStatementDto | null>(null);
  protected readonly customers = signal<Contact[]>([]);

  protected readonly contactId = signal(this.route.snapshot.queryParamMap.get('contactId') ?? '');
  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Customer').subscribe({ next: (c) => this.customers.set(c) });

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

  private runExport(full: boolean, page: number, pageSize: number): void {
    if (!this.contactId()) {
      return;
    }
    this.exporting.set(true);
    this.contactsService
      .exportCustomerStatement(this.organizationId, this.contactId(), this.fromDate(), this.toDate(), full, page, pageSize)
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `CustomerStatement_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Customer Statement.');
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
      .getCustomerStatement(this.organizationId, this.contactId(), this.fromDate(), this.toDate(), this.page(), this.pageSize())
      .subscribe({
        next: (statement) => {
          this.statement.set(statement);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Customer Statement.');
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
