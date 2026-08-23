import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { ContactGroup, ContactPersonnelRowDto } from '../../../core/contacts/contacts.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';

/** Contact Personnel tab (roadmap Phase 18) -- same shared-child-component-in-a-tab pattern as
 * TaskList/DealList (contact-detail-page's Tasks/Deals tabs): an inline create/edit form + a
 * paginated table, scoped to one Contact. */
@Component({
  selector: 'app-contact-personnel-list',
  imports: [ReactiveFormsModule, PaginationControl],
  templateUrl: './contact-personnel-list.html',
})
export class ContactPersonnelList implements OnInit {
  private readonly contactsService = inject(ContactsService);
  private readonly fb = inject(FormBuilder);

  readonly organizationId = input.required<string>();
  readonly contactId = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<ContactPersonnelRowDto[]>([]);
  protected readonly groups = signal<ContactGroup[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly showForm = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly saving = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    address: [''],
    code: [''],
    phone: [''],
    groupId: [''],
    email: [''],
    organizationTitle: [''],
  });

  ngOnInit(): void {
    this.contactsService.listContactGroups(this.organizationId()).subscribe({ next: (g) => this.groups.set(g) });
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

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', address: '', code: '', phone: '', groupId: '', email: '', organizationTitle: '' });
    this.showForm.set(true);
  }

  protected startEdit(row: ContactPersonnelRowDto): void {
    this.editingId.set(row.id);
    this.form.reset({
      name: row.name,
      address: row.address ?? '',
      code: row.code ?? '',
      phone: row.phone ?? '',
      groupId: row.groupId ?? '',
      email: row.email ?? '',
      organizationTitle: row.organizationTitle ?? '',
    });
    this.showForm.set(true);
  }

  protected cancelForm(): void {
    this.showForm.set(false);
    this.editingId.set(null);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, address, code, phone, groupId, email, organizationTitle } = this.form.getRawValue();
    const request = {
      name,
      address: address || null,
      code: code || null,
      phone: phone || null,
      groupId: groupId || null,
      email: email || null,
      organizationTitle: organizationTitle || null,
    };

    const editingId = this.editingId();
    const request$ = editingId
      ? this.contactsService.updateContactPersonnel(this.organizationId(), this.contactId(), editingId, request)
      : this.contactsService.createContactPersonnel(this.organizationId(), this.contactId(), request);

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.showForm.set(false);
        this.editingId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save contact person. Please try again.');
      },
    });
  }

  protected remove(row: ContactPersonnelRowDto): void {
    if (!window.confirm(`Remove "${row.name}"? This cannot be undone.`)) {
      return;
    }
    this.contactsService.deleteContactPersonnel(this.organizationId(), this.contactId(), row.id).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not remove contact person. Please try again.');
      },
    });
  }

  protected groupName(groupId: string | null): string {
    if (!groupId) {
      return '—';
    }
    return this.groups().find((g) => g.id === groupId)?.name ?? '—';
  }

  private load(): void {
    this.loading.set(true);
    this.contactsService.listContactPersonnel(this.organizationId(), this.contactId(), this.page(), this.pageSize()).subscribe({
      next: (result) => {
        this.rows.set(result.rows);
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load contact personnel.');
      },
    });
  }
}
