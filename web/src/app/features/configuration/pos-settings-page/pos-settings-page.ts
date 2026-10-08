import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { Account } from '../../../core/accounting/accounting.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { extractErrorMessage } from '../../../core/auth/api-error';
import { PaymentMode } from '../../../core/configuration/configuration.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { PosMode } from '../../../core/organizations/organizations.models';
import {
  DEFAULT_DENOMINATIONS,
  POS_MODE_LABELS,
  POS_TAB_LABELS,
  PosConfiguration,
  PosLocationSettings,
  PosTab,
} from '../../../core/pos/pos.models';
import { PosService } from '../../../core/pos/pos.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 60 -- Configurations > Point of Sale: the vendor's Location Settings (General and Payment
 * Mode tabs) plus the location's POS type, which the vendor puts on its Locations form and this app
 * sets here, where it acts (phase-60-status.md). One location at a time, picked at the top.
 *
 * <p>Every value the template branches on is its own signal written by the control's handler, never
 * a `computed()` over a form control's value -- the app is zoneless and such a computed caches
 * forever (phase 17).</p>
 */
@Component({
  selector: 'app-pos-settings-page',
  imports: [RouterLink, StatusBanner],
  templateUrl: './pos-settings-page.html',
})
export class PosSettingsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly posService = inject(PosService);
  private readonly configurationService = inject(ConfigurationService);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly modeLabels = POS_MODE_LABELS;
  protected readonly tabLabels = POS_TAB_LABELS;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  protected readonly configuration = signal<PosConfiguration | null>(null);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly paymentModes = signal<PaymentMode[]>([]);

  protected readonly selectedLocationId = signal<string | null>(null);
  protected readonly settings = signal<PosLocationSettings | null>(null);

  // The editable copy of the selected location's settings.
  protected readonly posMode = signal<PosMode>('None');
  protected readonly serviceChargeEnabled = signal(false);
  protected readonly serviceChargeRate = signal(0);
  protected readonly serviceChargeAccountId = signal('');
  /** Phase 68 -- on by default: a parcelled dine-in item keeps the charge it was ordered with. */
  protected readonly serviceChargeOnTakeAway = signal(true);
  protected readonly roundOffEnabled = signal(false);
  protected readonly roundOffAccountId = signal('');
  protected readonly cashVerificationRequired = signal(false);
  protected readonly denominations = signal<number[]>([]);
  protected readonly newDenomination = signal('');
  protected readonly defaultTab = signal<PosTab | ''>('');
  protected readonly printEstimateBill = signal(true);
  protected readonly printInvoice = signal(true);
  protected readonly printCreditNote = signal(true);
  protected readonly printKot = signal(true);
  protected readonly abbreviatedTaxInvoiceEnabled = signal(false);
  protected readonly linkedModeIds = signal<ReadonlySet<string>>(new Set());

  /** The modes the tenant is entitled to offer, plus None, which is always allowed. */
  protected readonly availableModes = computed<PosMode[]>(() => {
    const config = this.configuration();
    const modes: PosMode[] = ['None'];
    if (config?.posRetailEnabled) modes.push('Retail');
    if (config?.posRestaurantEnabled) modes.push('Restaurant');
    return modes;
  });

  protected readonly sortedAccounts = computed(() => [...this.accounts()].sort((a, b) => a.code.localeCompare(b.code)));

  /** A mode can be offered at a till only when it is active and names its payment account. */
  protected readonly modeRows = computed(() =>
    [...this.paymentModes()]
      .sort((a, b) => a.name.localeCompare(b.name))
      .map((mode) => ({ mode, usable: mode.isActive && mode.accountId !== null })));

  constructor() {
    this.load();
  }

  protected selectLocation(locationId: string): void {
    this.selectedLocationId.set(locationId || null);
    this.successMessage.set(null);
    this.errorMessage.set(null);

    if (!locationId) {
      this.settings.set(null);
      return;
    }

    this.posService.getLocationSettings(this.organizationId, locationId).subscribe({
      next: (settings) => this.apply(settings),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the location settings.'),
    });
  }

  protected saveMode(): void {
    const locationId = this.selectedLocationId();
    if (!locationId) return;

    this.begin();
    this.posService.setLocationMode(this.organizationId, locationId, this.posMode()).subscribe({
      next: (settings) => {
        this.apply(settings);
        this.patchSummary(locationId, { posMode: settings.posMode });
        this.succeed(`${settings.locationName} now runs: ${this.modeLabels[settings.posMode]}.`);
      },
      error: (err: unknown) => this.fail(err, 'Could not change the POS type.'),
    });
  }

  protected saveSettings(): void {
    const locationId = this.selectedLocationId();
    if (!locationId) return;

    this.begin();
    this.posService
      .updateLocationSettings(this.organizationId, locationId, {
        serviceChargeEnabled: this.serviceChargeEnabled(),
        serviceChargeRate: this.serviceChargeEnabled() ? this.serviceChargeRate() : 0,
        serviceChargeAccountId: this.serviceChargeEnabled() ? this.serviceChargeAccountId() || null : null,
        serviceChargeOnTakeAway: this.serviceChargeOnTakeAway(),
        roundOffEnabled: this.roundOffEnabled(),
        roundOffAccountId: this.roundOffEnabled() ? this.roundOffAccountId() || null : null,
        cashVerificationRequired: this.cashVerificationRequired(),
        denominations: this.denominations(),
        defaultTab: this.defaultTab() || null,
        printEstimateBill: this.printEstimateBill(),
        printInvoice: this.printInvoice(),
        printCreditNote: this.printCreditNote(),
        printKot: this.printKot(),
        abbreviatedTaxInvoiceEnabled: this.abbreviatedTaxInvoiceEnabled(),
      })
      .subscribe({
        next: (settings) => {
          this.apply(settings);
          this.patchSummary(locationId, { hasSettings: true });
          this.succeed(`Settings saved for ${settings.locationName}.`);
        },
        error: (err: unknown) => this.fail(err, 'Could not save the settings.'),
      });
  }

  protected savePaymentModes(): void {
    const locationId = this.selectedLocationId();
    if (!locationId) return;

    this.begin();
    this.posService.setLocationPaymentModes(this.organizationId, locationId, [...this.linkedModeIds()]).subscribe({
      next: (settings) => {
        this.apply(settings);
        this.patchSummary(locationId, { linkedPaymentModeCount: settings.paymentModeIds.length });
        this.succeed(`Payment modes saved for ${settings.locationName}.`);
      },
      error: (err: unknown) => this.fail(err, 'Could not save the payment modes.'),
    });
  }

  protected toggleMode(id: string, checked: boolean): void {
    const next = new Set(this.linkedModeIds());
    if (checked) next.add(id);
    else next.delete(id);
    this.linkedModeIds.set(next);
  }

  protected addDenomination(): void {
    const value = Number(this.newDenomination());
    if (!Number.isInteger(value) || value <= 0) {
      this.errorMessage.set('A denomination must be a positive whole number.');
      return;
    }
    if (this.denominations().includes(value)) {
      this.errorMessage.set(`${value} is already listed.`);
      return;
    }
    this.errorMessage.set(null);
    this.denominations.set([...this.denominations(), value].sort((a, b) => b - a));
    this.newDenomination.set('');
  }

  protected removeDenomination(value: number): void {
    this.denominations.set(this.denominations().filter((x) => x !== value));
  }

  protected resetDenominations(): void {
    this.denominations.set([...DEFAULT_DENOMINATIONS]);
  }

  protected numberOf(event: Event): number {
    return Number((event.target as HTMLInputElement).value);
  }

  protected valueOf(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected checkedOf(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  private apply(settings: PosLocationSettings): void {
    this.settings.set(settings);
    this.posMode.set(settings.posMode);
    this.serviceChargeEnabled.set(settings.serviceChargeEnabled);
    this.serviceChargeRate.set(settings.serviceChargeRate);
    this.serviceChargeAccountId.set(settings.serviceChargeAccountId ?? '');
    this.serviceChargeOnTakeAway.set(settings.serviceChargeOnTakeAway);
    this.roundOffEnabled.set(settings.roundOffEnabled);
    this.roundOffAccountId.set(settings.roundOffAccountId ?? '');
    this.cashVerificationRequired.set(settings.cashVerificationRequired);
    this.denominations.set([...settings.denominations]);
    this.defaultTab.set(settings.defaultTab ?? '');
    this.printEstimateBill.set(settings.printEstimateBill);
    this.printInvoice.set(settings.printInvoice);
    this.printCreditNote.set(settings.printCreditNote);
    this.printKot.set(settings.printKot);
    this.abbreviatedTaxInvoiceEnabled.set(settings.abbreviatedTaxInvoiceEnabled);
    this.linkedModeIds.set(new Set(settings.paymentModeIds));
  }

  private patchSummary(locationId: string, patch: Partial<PosConfiguration['locations'][number]>): void {
    const config = this.configuration();
    if (!config) return;
    this.configuration.set({
      ...config,
      locations: config.locations.map((x) => (x.id === locationId ? { ...x, ...patch } : x)),
    });
  }

  private begin(): void {
    this.saving.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);
  }

  private succeed(message: string): void {
    this.saving.set(false);
    this.successMessage.set(message);
  }

  private fail(err: unknown, fallback: string): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }

  private load(): void {
    this.posService.getConfiguration(this.organizationId).subscribe({
      next: (config) => {
        this.configuration.set(config);
        this.loading.set(false);
        const first = config.locations.find((x) => x.isActive) ?? config.locations[0];
        if (first) this.selectLocation(first.id);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load point-of-sale configuration.');
      },
    });

    this.accountingService.listAllAccounts(this.organizationId).subscribe({ next: (accounts) => this.accounts.set(accounts) });
    this.configurationService.listPaymentModes(this.organizationId).subscribe({ next: (modes) => this.paymentModes.set(modes) });
  }
}
