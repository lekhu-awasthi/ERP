import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { Account } from '../../../core/accounting/accounting.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { PAYMENT_MODE_KINDS, PaymentMode, PaymentModeKind } from '../../../core/configuration/configuration.models';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { LookupFilter, matchesLookup } from '../../../shared/pagination/lookup-filter';

/** Roadmap Phase 2 exit criteria: Admin can create/edit/delete a PaymentMode through this screen. */
@Component({
  selector: 'app-payment-mode-list-page',
  imports: [ReactiveFormsModule, RouterLink, StatusBanner, LookupFilter],
  templateUrl: './payment-mode-list-page.html',
})
export class PaymentModeListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly configurationService = inject(ConfigurationService);
  private readonly accountingService = inject(AccountingService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<PaymentMode[]>([]);
  /** Phase 40 — 34b's carried item #3. Client-side, because `listAll` already fetched every row. */
  protected readonly term = signal('');

  protected readonly filtered = computed(() =>
    this.items().filter((item) => matchesLookup(this.term(), item.name)));
  protected readonly editingId = signal<string | null>(null);

  /** Phase 60 -- the vendor's "Payment Account" list: cash and bank accounts only. */
  protected readonly kinds = PAYMENT_MODE_KINDS;
  protected readonly paymentAccounts = signal<Account[]>([]);
  protected readonly accountNames = computed(
    () => new Map(this.paymentAccounts().map((a) => [a.id, `${a.code} — ${a.name}`] as const)));
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    isActive: [true],
    requiresChequeDetails: [false],
    kind: ['Other' as PaymentModeKind],
    accountId: [''],
  });

  constructor() {
    this.load();
    this.accountingService.listAllAccounts(this.organizationId, 'Asset').subscribe({
      next: (accounts) => this.paymentAccounts.set(
        accounts.filter((a) => a.kind === 'Cash' || a.kind === 'Bank').sort((a, b) => a.code.localeCompare(b.code))),
    });
  }

  protected kindLabel(kind: PaymentModeKind): string {
    return this.kinds.find((k) => k.value === kind)?.label ?? kind;
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', isActive: true, requiresChequeDetails: false, kind: 'Other', accountId: '' });
  }

  protected startEdit(item: PaymentMode): void {
    this.editingId.set(item.id);
    this.form.reset({
      name: item.name,
      isActive: item.isActive,
      requiresChequeDetails: item.requiresChequeDetails,
      kind: item.kind,
      accountId: item.accountId ?? '',
    });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, isActive, requiresChequeDetails, kind, accountId: rawAccountId } = this.form.getRawValue();
    const accountId = rawAccountId || null;
    const editingId = this.editingId();

    const request$ = editingId
      ? this.configurationService.updatePaymentMode(this.organizationId, editingId, {
          name, isActive, requiresChequeDetails, kind, accountId,
        })
      : this.configurationService.createPaymentMode(this.organizationId, { name, requiresChequeDetails, kind, accountId });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save payment mode. Please try again.');
      },
    });
  }

  protected requestDelete(item: PaymentMode): void {
    this.confirmingDeleteId.set(item.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(item: PaymentMode): void {
    this.configurationService.deletePaymentMode(this.organizationId, item.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete payment mode. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.configurationService.listPaymentModes(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load payment modes.');
      },
    });
  }
}
