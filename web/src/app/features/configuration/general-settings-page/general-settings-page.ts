import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import {
  BalanceAction,
  GeneralSettings,
  ProductPriceBasis,
  SuggestSellingPriceMode,
} from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';

/**
 * Configurations > General (phase 31). The five behaviour switches that decide what happens at
 * Approve, live-confirmed 2026-09-06.
 *
 * Four of them had been on `TenantSettings` since phase 2 with no command, no endpoint and no
 * screen -- so a tenant could never change them, and three were read by nothing at all. The fifth,
 * Negative Item Balance, was read by `FifoStockAvailabilityPolicy` but was equally stuck on its
 * seeded default. This page is what makes the rest of the phase reachable; phase 29's lesson, that a
 * tenant-level field with no screen behind it is not shipped.
 *
 * The reference page auto-saves each radio the instant it is clicked and has no Save button. This
 * one keeps an explicit Save: the whole form is one command over one row, an accidental click on a
 * radio that turns a hard stop into a silent allow across the whole organization should not be
 * instantly live, and every other settings screen in this app (Accounting Defaults, Lock Date) works
 * this way.
 *
 * Inventory Tracking Mode is deliberately absent from the UI while it is present on the command:
 * the reference product removed that control from this page, and this codebase ships
 * AccountingMovement only (the Delivery Note / GRN entry in roadmap.md's deferred list is the
 * re-entry condition). Sending the loaded value straight back keeps the field writable by the API
 * without offering a choice nothing honours yet.
 */
@Component({
  selector: 'app-general-settings-page',
  imports: [RouterLink],
  templateUrl: './general-settings-page.html',
})
export class GeneralSettingsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  protected readonly settings = signal<GeneralSettings | null>(null);

  protected readonly suggestSellingPriceMode = signal<SuggestSellingPriceMode>('RecentSellingPrice');
  protected readonly productPriceBasis = signal<ProductPriceBasis>('ExclusiveOfVat');
  protected readonly negativeCashBalanceAction = signal<BalanceAction>('Reject');
  protected readonly negativeStockBalanceAction = signal<BalanceAction>('Warn');
  protected readonly creditLimitExceedsAction = signal<BalanceAction>('Warn');

  /** The three-way groups all render the same way, so the option copy lives here once. Wording is
   *  taken from the live page verbatim. */
  protected readonly balanceOptions: ReadonlyArray<{ value: BalanceAction; label: string; help: string }> = [
    { value: 'Reject', label: 'Reject', help: 'The transaction will not be allowed.' },
    { value: 'Warn', label: 'Warn', help: 'Allowed, with a warning message the user must confirm.' },
    { value: 'DoNothing', label: 'Do Nothing', help: 'Allowed, with no warning at all.' },
  ];

  constructor() {
    this.load();
  }

  protected setSuggestSellingPriceMode(value: SuggestSellingPriceMode): void {
    this.suggestSellingPriceMode.set(value);
  }

  protected setProductPriceBasis(value: ProductPriceBasis): void {
    this.productPriceBasis.set(value);
  }

  protected setNegativeCashBalanceAction(value: BalanceAction): void {
    this.negativeCashBalanceAction.set(value);
  }

  protected setNegativeStockBalanceAction(value: BalanceAction): void {
    this.negativeStockBalanceAction.set(value);
  }

  protected setCreditLimitExceedsAction(value: BalanceAction): void {
    this.creditLimitExceedsAction.set(value);
  }

  protected save(): void {
    const current = this.settings();
    if (!current) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.organizationsService
      .updateGeneralSettings(this.organizationId, {
        suggestSellingPriceMode: this.suggestSellingPriceMode(),
        productPriceBasis: this.productPriceBasis(),
        // Not offered on this screen -- see the class comment.
        inventoryTrackingMode: current.inventoryTrackingMode,
        negativeCashBalanceAction: this.negativeCashBalanceAction(),
        negativeStockBalanceAction: this.negativeStockBalanceAction(),
        creditLimitExceedsAction: this.creditLimitExceedsAction(),
      })
      .subscribe({
        next: (result) => {
          this.saving.set(false);
          this.apply(result);
          this.successMessage.set('General settings saved.');
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save settings. Please try again.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);

    this.organizationsService.getGeneralSettings(this.organizationId).subscribe({
      next: (result) => {
        this.apply(result);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load settings.');
      },
    });
  }

  private apply(result: GeneralSettings): void {
    this.settings.set(result);
    this.suggestSellingPriceMode.set(result.suggestSellingPriceMode);
    this.productPriceBasis.set(result.productPriceBasis);
    this.negativeCashBalanceAction.set(result.negativeCashBalanceAction);
    this.negativeStockBalanceAction.set(result.negativeStockBalanceAction);
    this.creditLimitExceedsAction.set(result.creditLimitExceedsAction);
  }
}
