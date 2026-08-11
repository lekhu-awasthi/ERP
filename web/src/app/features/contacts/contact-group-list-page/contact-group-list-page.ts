import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { buildTreeRows, TreeRow } from '../../../core/common/tree';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { ContactGroup } from '../../../core/contacts/contacts.models';

type ContactGroupRow = TreeRow<ContactGroup>;

/** Establishes the flat list-page CRUD pattern (credit-term-list-page) for a self-referencing
 * tree -- indentation is computed client-side from ParentGroupId rather than via a server-side
 * subtree query (see phase-3-status.md's scope decisions: no ITreeQuery<T> yet). */
@Component({
  selector: 'app-contact-group-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './contact-group-list-page.html',
})
export class ContactGroupListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly contactsService = inject(ContactsService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<ContactGroup[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly rows = computed<ContactGroupRow[]>(() =>
    buildTreeRows(
      this.items(),
      (group) => group.id,
      (group) => group.parentGroupId,
      (group) => group.name,
    ),
  );

  protected readonly parentOptions = computed<ContactGroupRow[]>(() => {
    const editingId = this.editingId();
    return this.rows().filter((row) => row.item.id !== editingId);
  });

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    parentGroupId: [''],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', parentGroupId: '', isActive: true });
  }

  protected startEdit(group: ContactGroup): void {
    this.editingId.set(group.id);
    this.form.reset({ name: group.name, parentGroupId: group.parentGroupId ?? '', isActive: group.isActive });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, parentGroupId, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    const request$ = editingId
      ? this.contactsService.updateContactGroup(this.organizationId, editingId, {
          name,
          parentGroupId: parentGroupId || null,
          isActive,
        })
      : this.contactsService.createContactGroup(this.organizationId, { name, parentGroupId: parentGroupId || null });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save contact group. Please try again.');
      },
    });
  }

  protected requestDelete(group: ContactGroup): void {
    this.confirmingDeleteId.set(group.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(group: ContactGroup): void {
    this.contactsService.deleteContactGroup(this.organizationId, group.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete contact group. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.contactsService.listContactGroups(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load contact groups.');
      },
    });
  }
}
