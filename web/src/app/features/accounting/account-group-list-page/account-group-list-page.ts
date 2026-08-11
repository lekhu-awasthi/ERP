import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { buildTreeRows, TreeRow } from '../../../core/common/tree';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { AccountGroup, AccountRootType } from '../../../core/accounting/accounting.models';

type AccountGroupRow = TreeRow<AccountGroup>;

/** Same tree list-page pattern as ContactGroups/ProductCategories -- see contact-group-list-page's
 * doc comment for the indentation approach. RootType is immutable after Create (see AccountGroup's
 * Domain doc comment), so it's only offered on the create form and shown read-only once picked;
 * the parent picker is filtered to same-root-type groups so a mismatched parent/child pair can
 * never even be selected client-side (still re-validated server-side regardless). */
@Component({
  selector: 'app-account-group-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './account-group-list-page.html',
})
export class AccountGroupListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<AccountGroup[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly rootTypes: AccountRootType[] = ['Asset', 'Liability', 'Equity', 'Income', 'Expense'];

  protected readonly rows = computed<AccountGroupRow[]>(() =>
    buildTreeRows(
      this.items(),
      (group) => group.id,
      (group) => group.parentGroupId,
      (group) => group.name,
    ),
  );

  // A plain signal (not read off the FormControl directly) so parentOptions recomputes as the
  // user picks a different RootType on the create form -- FormControl.value isn't itself a
  // Signal, so a computed() reading it wouldn't re-run on (change).
  protected readonly selectedRootType = signal<AccountRootType>('Asset');

  protected readonly parentOptions = computed<AccountGroupRow[]>(() => {
    const editingId = this.editingId();
    const rootType = this.selectedRootType();
    return this.rows().filter((row) => row.item.id !== editingId && row.item.rootType === rootType);
  });

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    rootType: ['Asset' as AccountRootType, Validators.required],
    parentGroupId: [''],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected onRootTypeChange(): void {
    this.selectedRootType.set(this.form.controls.rootType.value);
    this.form.controls.parentGroupId.setValue('');
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.selectedRootType.set('Asset');
    this.form.reset({ name: '', rootType: 'Asset', parentGroupId: '', isActive: true });
  }

  protected startEdit(group: AccountGroup): void {
    this.editingId.set(group.id);
    this.selectedRootType.set(group.rootType);
    this.form.reset({
      name: group.name,
      rootType: group.rootType,
      parentGroupId: group.parentGroupId ?? '',
      isActive: group.isActive,
    });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, rootType, parentGroupId, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    const onSuccess = (): void => {
      this.saving.set(false);
      this.startCreate();
      this.load();
    };
    const onError = (err: unknown): void => {
      this.saving.set(false);
      this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save account group. Please try again.');
    };

    // Not a single ternary-assigned request$ -- see account-list-page.ts's identical comment
    // (CreateAccountGroupResult/UpdateAccountGroupResult also differ in shape).
    if (editingId) {
      this.accountingService
        .updateAccountGroup(this.organizationId, editingId, { name, parentGroupId: parentGroupId || null, isActive })
        .subscribe({ next: onSuccess, error: onError });
    } else {
      this.accountingService
        .createAccountGroup(this.organizationId, { name, rootType, parentGroupId: parentGroupId || null })
        .subscribe({ next: onSuccess, error: onError });
    }
  }

  protected requestDelete(group: AccountGroup): void {
    this.confirmingDeleteId.set(group.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(group: AccountGroup): void {
    this.accountingService.deleteAccountGroup(this.organizationId, group.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete account group. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.accountingService.listAccountGroups(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load account groups.');
      },
    });
  }
}
