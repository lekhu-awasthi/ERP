import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact, ContactStatementDto } from '../../../core/contacts/contacts.models';

/**
 * Read-only report screen -- roadmap Phase 9's ContactStatementQuery (ContactType=Supplier).
 * Confirmed live directly against the reference product's own Supplier Statement screen during this
 * phase's design pass -- identical Txn Date/Txn Type/Txn No/Reference No/Debit/Credit/Balance columns
 * to Customer's confirmed shape, an Opening Balance row and a Closing Balance row, balances suffixed
 * "DR"/"CR". Debit/Credit follow real double-entry polarity -- AP is credit-normal for a Supplier,
 * the exact opposite of Customer Statement's AR-debit-normal polarity (confirmed directly against the
 * live screen: its Opening Balance row carried its value in the Credit column) -- computed
 * server-side, this page only formats already-authoritative numbers.
 */
@Component({
  selector: 'app-supplier-statement-page',
  imports: [RouterLink],
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

  constructor() {
    this.contactsService.listContacts(this.organizationId, 'Supplier').subscribe({ next: (c) => this.suppliers.set(c) });

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

    this.contactsService.getSupplierStatement(this.organizationId, this.contactId(), this.fromDate(), this.toDate()).subscribe({
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
