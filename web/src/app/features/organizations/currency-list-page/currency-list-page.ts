import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import {
  BASE_CURRENCY_CODE,
  Currency,
  CurrencyCatalogEntry,
} from '../../../core/organizations/organizations.models';

/**
 * Phase 28 (FR-2.5) -- Organization > Features > Multiple Currency, as read live on 2026-09-04:
 * a Code / Name / Symbol table with an ADD NEW CURRENCY action whose dialog is a catalog picker
 * plus an editable Name and Symbol that the picker pre-fills.
 *
 * The base currency row is rendered with its actions suppressed rather than hidden, matching this
 * codebase's phase-20f rule of showing what cannot be changed instead of pretending it is absent --
 * the server refuses to deactivate or delete it either, so the two agree.
 *
 * Adding a currency is Admin-only and additionally capped by the MultiCurrency entitlement, so a
 * flag-off tenant gets a 403 naming the feature; the page surfaces that message rather than
 * pre-hiding the button, because the entitlement is not readable from this component and a button
 * that silently disappears is harder to explain than an error that names the reason.
 */
@Component({
  selector: 'app-currency-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './currency-list-page.html',
})
export class CurrencyListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly fb = inject(FormBuilder);

  protected readonly baseCurrencyCode = BASE_CURRENCY_CODE;
  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<Currency[]>([]);
  protected readonly catalog = signal<CurrencyCatalogEntry[]>([]);
  protected readonly editingId = signal<string | null>(null);

  /** The picker offers only what this tenant has not already activated -- the live dialog's own
   * behaviour, and the reason it renders empty once everything is on the list. */
  protected readonly available = computed(() => this.catalog().filter((x) => !x.alreadyActivated));

  protected readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required]],
    name: ['', [Validators.required, Validators.maxLength(60)]],
    symbol: ['', [Validators.required, Validators.maxLength(10)]],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  /** Picking a catalog entry pre-fills Name and Symbol, exactly as the live dialog does -- and
   * leaves them editable afterwards, which is why they are stored on the row rather than derived
   * from the code at render time. */
  protected onCodeSelected(code: string): void {
    const entry = this.catalog().find((x) => x.code === code);
    this.form.patchValue({ code, name: entry?.name ?? '', symbol: entry?.symbol ?? '' });
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ code: '', name: '', symbol: '', isActive: true });
    this.form.controls.code.enable();
  }

  protected startEdit(item: Currency): void {
    this.editingId.set(item.id);
    this.form.reset({ code: item.code, name: item.name, symbol: item.symbol, isActive: item.isActive });
    // The code is the identity every document stores, so it is never editable.
    this.form.controls.code.disable();
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { code, name, symbol, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    if (editingId) {
      this.organizationsService
        .updateCurrency(this.organizationId, editingId, { name, symbol, isActive })
        .subscribe({
          next: () => this.onSaved(),
          error: (err: unknown) => this.onSaveFailed(err),
        });
      return;
    }

    this.organizationsService.createCurrency(this.organizationId, { code, name, symbol }).subscribe({
      next: () => this.onSaved(),
      error: (err: unknown) => this.onSaveFailed(err),
    });
  }

  protected isBase(item: Currency): boolean {
    return item.code === BASE_CURRENCY_CODE;
  }

  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected requestDelete(item: Currency): void {
    this.confirmingDeleteId.set(item.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(item: Currency): void {
    this.organizationsService.deleteCurrency(this.organizationId, item.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not remove currency. Please try again.');
      },
    });
  }

  private onSaved(): void {
    this.saving.set(false);
    this.startCreate();
    this.load();
  }

  private onSaveFailed(err: unknown): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save currency. Please try again.');
  }

  private load(): void {
    this.loading.set(true);
    this.organizationsService.listCurrencies(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load currencies.');
      },
    });

    this.organizationsService.listCurrencyCatalog(this.organizationId).subscribe({
      next: (entries) => this.catalog.set(entries),
      error: () => this.catalog.set([]),
    });
  }
}
