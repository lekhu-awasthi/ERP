import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact, ContactStatementDto } from '../../../core/contacts/contacts.models';

/**
 * Read-only report screen -- roadmap Phase 9's ContactStatementQuery (ContactType=Customer), the
 * real running-balance ledger architecture-spec.md §4.2 names and every prior Phase 8 report's
 * Opening/Closing Balance approximation deferred to. Confirmed live shape (architecture-spec.md
 * line 277): a full running-balance ledger, Opening Balance row + every transaction row. Single
 * Contact at a time -- see ContactStatementQuery's own doc comment on why this simplifies the live
 * screen's multi-select bulk-print convenience down to one-Account-at-a-time. Debit/Credit follow
 * real double-entry polarity (AR is debit-normal for a Customer), computed server-side along with
 * the DR/CR balance suffix -- this page only formats already-authoritative numbers.
 */
@Component({
  selector: 'app-customer-statement-page',
  imports: [RouterLink],
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

  constructor() {
    this.contactsService.listContacts(this.organizationId, 'Customer').subscribe({ next: (c) => this.customers.set(c) });

    // Pre-filled from the Contact Overview tab's "View Full Statement" link -- the sensible default
    // date range is this page's own existing first-of-month-to-today default, not a new convention.
    if (this.contactId()) {
      this.load();
    }
  }

  protected onContactChange(event: Event): void {
    this.contactId.set((event.target as HTMLSelectElement).value);
    this.load();
  }

  protected onFromDateChange(event: Event): void {
    this.fromDate.set((event.target as HTMLInputElement).value);
    this.load();
  }

  protected onToDateChange(event: Event): void {
    this.toDate.set((event.target as HTMLInputElement).value);
    this.load();
  }

  private load(): void {
    if (!this.contactId()) {
      this.statement.set(null);
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);

    this.contactsService.getCustomerStatement(this.organizationId, this.contactId(), this.fromDate(), this.toDate()).subscribe({
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
