import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact, ContactType } from '../../../core/contacts/contacts.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { ListChrome } from '../../../shared/pagination/list-chrome';
import { ListFilter } from '../../../shared/pagination/list-query-options';

type ContactTypeFilter = ContactType | 'All';

/** List-page chrome for Contact -- rows navigate to the record-detail-page (contact-detail-page)
 * instead of inline-editing, establishing the list->detail split every later module reuses (see
 * phase-3-status.md). */
@Component({
  selector: 'app-contact-list-page',
  imports: [RouterLink, PaginationControl, ListChrome],
  templateUrl: './contact-list-page.html',
})
export class ContactListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly contactsService = inject(ContactsService);

  /**
   * Phase 34b -- the screen's search term. No date range: this list is master data, which
   * the shell's global range deliberately does not scope (see `IDateRangeFilteredQuery`).
   */
  protected readonly filter = new ListFilter();

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<Contact[]>([]);
  protected readonly typeFilter = signal<ContactTypeFilter>('All');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly types: ContactTypeFilter[] = ['All', 'Customer', 'Supplier', 'Lead'];

  constructor() {
    this.load();
  }

  protected selectType(type: ContactTypeFilter): void {
    this.typeFilter.set(type);
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

  private load(): void {
    this.loading.set(true);
    const type = this.typeFilter();
    this.contactsService
      .listContacts(this.organizationId, type === 'All' ? undefined : type, this.page(), this.pageSize(), this.filter.options())
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load contacts.');
        },
      });
  }
  /**
   * Phase 34b -- the shared list chrome's search box. Resets to page 1, because staying on page 4
   * of a result set the filter has just shrunk to one page shows an empty list and reads as
   * "search found nothing".
   */
  protected onSearch(term: string): void {
    this.filter.search.set(term);
    this.page.set(1);
    this.load();
  }

}
