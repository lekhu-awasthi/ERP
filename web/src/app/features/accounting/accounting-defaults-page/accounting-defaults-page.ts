import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';

/**
 * Minimal seam for Invoice/Payment's default-GL-account fallback (TenantSettings.
 * DefaultSalesAccountId/DefaultAccountsReceivableId/DefaultVatPayableAccountId) -- not a full
 * TenantSettings editor (that stays deferred, per roadmap Phase 2's own note), just the three
 * fields Phase 5's posting rules read. See phase-5-status.md's scope decision.
 */
@Component({
  selector: 'app-accounting-defaults-page',
  imports: [RouterLink],
  templateUrl: './accounting-defaults-page.html',
})
export class AccountingDefaultsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly accounts = signal<Account[]>([]);

  protected readonly defaultSalesAccountId = signal<string>('');
  protected readonly defaultAccountsReceivableId = signal<string>('');
  protected readonly defaultVatPayableAccountId = signal<string>('');

  protected readonly sortedAccounts = computed(() => [...this.accounts()].sort((a, b) => a.code.localeCompare(b.code)));

  constructor() {
    this.load();
  }

  protected save(): void {
    this.saving.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.organizationsService
      .updateAccountingDefaults(this.organizationId, {
        defaultSalesAccountId: this.defaultSalesAccountId() || null,
        defaultAccountsReceivableId: this.defaultAccountsReceivableId() || null,
        defaultVatPayableAccountId: this.defaultVatPayableAccountId() || null,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.successMessage.set('Accounting defaults saved.');
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save accounting defaults. Please try again.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);

    this.accountingService.listAccounts(this.organizationId).subscribe({ next: (accounts) => this.accounts.set(accounts) });

    this.organizationsService.getAccountingDefaults(this.organizationId).subscribe({
      next: (defaults) => {
        this.defaultSalesAccountId.set(defaults.defaultSalesAccountId ?? '');
        this.defaultAccountsReceivableId.set(defaults.defaultAccountsReceivableId ?? '');
        this.defaultVatPayableAccountId.set(defaults.defaultVatPayableAccountId ?? '');
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load accounting defaults.');
      },
    });
  }
}
