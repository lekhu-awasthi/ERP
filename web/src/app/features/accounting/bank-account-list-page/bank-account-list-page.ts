import { DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { AccountGroup, AccountKind, BankAccountDto } from '../../../core/accounting/accounting.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { Bank } from '../../../core/configuration/configuration.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';

/** Phase 17 -- card-grid view of every Bank/Cash-kind Account with a live running balance,
 * All/Inactive tabs (docs/phase-17-status.md decision #3). */
@Component({
  selector: 'app-bank-account-list-page',
  imports: [ReactiveFormsModule, RouterLink, PaginationControl, DecimalPipe],
  templateUrl: './bank-account-list-page.html',
})
export class BankAccountListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);
  private readonly configurationService = inject(ConfigurationService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<BankAccountDto[]>([]);
  protected readonly groups = signal<AccountGroup[]>([]);
  protected readonly banks = signal<Bank[]>([]);
  protected readonly showAddForm = signal(false);
  protected readonly activeTab = signal<'active' | 'inactive'>('active');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly form = this.fb.nonNullable.group({
    kind: ['Bank' as AccountKind, Validators.required],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    groupId: ['', Validators.required],
    bankId: [''],
    accountNumber: [''],
  });

  constructor() {
    this.accountingService.listAccountGroups(this.organizationId).subscribe({
      next: (g) => this.groups.set(g.filter((x) => x.rootType === 'Asset')),
    });
    this.configurationService.listBanks(this.organizationId).subscribe({ next: (b) => this.banks.set(b) });
    this.load();
  }

  protected switchTab(tab: 'active' | 'inactive'): void {
    this.activeTab.set(tab);
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

  protected openAddForm(): void {
    this.form.reset({ kind: 'Bank', name: '', groupId: this.groups()[0]?.id ?? '', bankId: '', accountNumber: '' });
    this.showAddForm.set(true);
  }

  protected closeAddForm(): void {
    this.showAddForm.set(false);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { kind, name, groupId, bankId, accountNumber } = this.form.getRawValue();

    this.accountingService
      .createAccount(this.organizationId, {
        name,
        groupId,
        kind,
        bankId: kind === 'Bank' && bankId ? bankId : null,
        accountNumber: accountNumber || null,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.showAddForm.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not create bank account. Please try again.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.accountingService
      .listBankAccounts(this.organizationId, this.activeTab() === 'active', this.page(), this.pageSize())
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load bank accounts.');
        },
      });
  }
}
