import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account, AccountGroup } from '../../../core/accounting/accounting.models';

/** List-page chrome for Account -- same "inline form above a flat list" shape as
 * account-group-list-page/product-category-list-page (Account is closer in weight to a simple
 * lookup than to Contact/Product's full record-detail-page, since it has few fields and no
 * Draft/Approve lifecycle of its own). */
@Component({
  selector: 'app-account-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './account-list-page.html',
})
export class AccountListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<Account[]>([]);
  protected readonly groups = signal<AccountGroup[]>([]);
  protected readonly editingId = signal<string | null>(null);

  protected readonly sortedItems = computed(() => [...this.items()].sort((a, b) => a.code.localeCompare(b.code)));

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    groupId: ['', Validators.required],
    isActive: [true],
  });

  constructor() {
    this.accountingService.listAccountGroups(this.organizationId).subscribe({
      next: (groups) => this.groups.set(groups),
    });
    this.load();
  }

  protected groupName(groupId: string): string {
    return this.groups().find((g) => g.id === groupId)?.name ?? '—';
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', groupId: '', isActive: true });
  }

  protected startEdit(account: Account): void {
    this.editingId.set(account.id);
    this.form.reset({ name: account.name, groupId: account.groupId, isActive: account.isActive });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, groupId, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    const onSuccess = (): void => {
      this.saving.set(false);
      this.startCreate();
      this.load();
    };
    const onError = (err: unknown): void => {
      this.saving.set(false);
      this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save account. Please try again.');
    };

    // Not a single ternary-assigned request$ -- CreateAccountResult/UpdateAccountResult have
    // different shapes, and Observable<A> | Observable<B>'s .subscribe() overload resolution
    // isn't reliably callable across the union (same class of TS quirk as the HttpClient params
    // gotcha in CLAUDE.md's known gotchas).
    if (editingId) {
      this.accountingService
        .updateAccount(this.organizationId, editingId, { name, groupId, isActive })
        .subscribe({ next: onSuccess, error: onError });
    } else {
      this.accountingService.createAccount(this.organizationId, { name, groupId }).subscribe({ next: onSuccess, error: onError });
    }
  }

  private load(): void {
    this.loading.set(true);
    this.accountingService.listAccounts(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load accounts.');
      },
    });
  }
}
