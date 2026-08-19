import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact, ContactType } from '../../../core/contacts/contacts.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';

type ContactTypeFilter = ContactType | 'All';

/** List-page chrome for Contact -- rows navigate to the record-detail-page (contact-detail-page)
 * instead of inline-editing, establishing the list->detail split every later module reuses (see
 * phase-3-status.md). */
@Component({
  selector: 'app-contact-list-page',
  imports: [RouterLink, PaginationControl],
  templateUrl: './contact-list-page.html',
})
export class ContactListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly contactsService = inject(ContactsService);

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
      .listContacts(this.organizationId, type === 'All' ? undefined : type, this.page(), this.pageSize())
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
}
