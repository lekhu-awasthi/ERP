import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account, AccountGroup } from '../../../core/accounting/accounting.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { ListChrome } from '../../../shared/pagination/list-chrome';
import { ListFilter } from '../../../shared/pagination/list-query-options';

/** List-page chrome for Account -- same "inline form above a flat list" shape as
 * account-group-list-page/product-category-list-page (Account is closer in weight to a simple
 * lookup than to Contact/Product's full record-detail-page, since it has few fields and no
 * Draft/Approve lifecycle of its own). */
@Component({
  selector: 'app-account-list-page',
  imports: [ReactiveFormsModule, RouterLink, PaginationControl, ListChrome],
  templateUrl: './account-list-page.html',
})
export class AccountListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);
  private readonly fb = inject(FormBuilder);

  /**
   * Phase 34b -- the screen's search term. No date range: this list is master data, which
   * the shell's global range deliberately does not scope (see `IDateRangeFilteredQuery`).
   */
  protected readonly filter = new ListFilter();

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<Account[]>([]);
  protected readonly groups = signal<AccountGroup[]>([]);
  protected readonly editingId = signal<string | null>(null);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

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

  protected onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.page.set(1);
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
    this.accountingService.listAccounts(this.organizationId, undefined, this.page(), this.pageSize(), this.filter.options()).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load accounts.');
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
