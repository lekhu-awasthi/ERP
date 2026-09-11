import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import {
  BillingLocation,
  BillingLocationSettings,
  LocationScopeMode,
  Warehouse,
} from '../../../core/organizations/organizations.models';
import { BillingLocationStore } from '../../../shared/locations/billing-location-store';

/**
 * Phase 32 (FR-2.3/FR-3.3) -- Organization > Features > Billing Location, as read live on
 * 2026-09-07 on `cadehi.tigg.app`, the first tenant this project has had access to with the
 * entitlement switched on. (The tenant the rest of the scan was taken from has it off, which is why
 * no earlier phase could read this screen and why the roadmap expected phase 32 to be derived
 * rather than observed.)
 *
 * Two halves, both live:
 * - a Code / Name / Address / Warehouse table with **+ ADD NEW LOCATION** and a **Show Inactive**
 *   toggle;
 * - an **Advanced** disclosure, collapsed by default -- "Choose how locations should be used in
 *   your organization" -- holding the scope radio and the report-permission checkbox. This is the
 *   control the user could not find on the live product, because a tenant without the entitlement
 *   is shown no panel at all.
 *
 * The HeadOffice row renders with its deactivate control suppressed rather than hidden, matching
 * the codebase's phase-20f rule of showing what cannot be changed instead of pretending it is
 * absent -- and the server refuses too, so the two agree.
 */
@Component({
  selector: 'app-billing-location-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './billing-location-list-page.html',
})
export class BillingLocationListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly locationStore = inject(BillingLocationStore);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly savingSettings = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<BillingLocation[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly settings = signal<BillingLocationSettings | null>(null);
  protected readonly editingId = signal<string | null>(null);

  /** The app is zoneless, so a value the template branches on lives in its own signal rather than a
   * computed() over a FormControl -- the phase-17 rule. */
  protected readonly showInactive = signal(false);
  protected readonly advancedOpen = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(20)]],
    name: ['', [Validators.required, Validators.maxLength(100)]],
    address: ['', [Validators.required, Validators.maxLength(250)]],
    warehouseId: [''],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected toggleShowInactive(): void {
    this.showInactive.update((x) => !x);
    this.load();
  }

  protected toggleAdvanced(): void {
    this.advancedOpen.update((x) => !x);
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ code: '', name: '', address: '', warehouseId: '', isActive: true });
    this.form.controls.address.addValidators(Validators.required);
    this.form.controls.address.updateValueAndValidity();
  }

  protected startEdit(item: BillingLocation): void {
    this.editingId.set(item.id);
    this.form.reset({
      code: item.code,
      name: item.name,
      address: item.address ?? '',
      warehouseId: item.warehouseId ?? '',
      isActive: item.isActive,
    });

    // The seeded HeadOffice row has no address, and an Admin renaming it or pointing it at a
    // warehouse must not be forced to invent one -- the same asymmetry the two server validators
    // have (required on create, optional on update).
    this.form.controls.address.removeValidators(Validators.required);
    this.form.controls.address.updateValueAndValidity();
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { code, name, address, warehouseId, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    if (editingId) {
      this.organizationsService
        .updateBillingLocation(this.organizationId, editingId, {
          code,
          name,
          address: address || null,
          warehouseId: warehouseId || null,
          isActive,
        })
        .subscribe({
          next: () => this.onSaved(),
          error: (err: unknown) => this.onSaveFailed(err),
        });
      return;
    }

    this.organizationsService
      .createBillingLocation(this.organizationId, {
        code,
        name,
        address: address || null,
        warehouseId: warehouseId || null,
      })
      .subscribe({
        next: () => this.onSaved(),
        error: (err: unknown) => this.onSaveFailed(err),
      });
  }

  protected setScopeMode(mode: LocationScopeMode): void {
    const current = this.settings();
    if (!current || current.locationScopeMode === mode) {
      return;
    }

    this.saveSettings(mode, current.locationWiseReportPermission);
  }

  protected toggleReportPermission(): void {
    const current = this.settings();
    if (!current) {
      return;
    }

    this.saveSettings(current.locationScopeMode, !current.locationWiseReportPermission);
  }

  private saveSettings(mode: LocationScopeMode, reportPermission: boolean): void {
    this.savingSettings.set(true);
    this.errorMessage.set(null);

    this.organizationsService
      .updateBillingLocationSettings(this.organizationId, {
        locationScopeMode: mode,
        locationWiseReportPermission: reportPermission,
      })
      .subscribe({
        next: (updated) => {
          this.settings.set(updated);
          this.savingSettings.set(false);
          // Phase 35a -- the scope mode decides which document types show a picker at all, and the
          // store caches the resolved list; flipping the radio must reach every form.
          this.locationStore.invalidate(this.organizationId);
        },
        error: (err: unknown) => {
          this.savingSettings.set(false);
          this.errorMessage.set(
            extractErrorMessage(err) ?? 'Could not save these location settings. Please try again.',
          );
        },
      });
  }

  private onSaved(): void {
    this.saving.set(false);

    // Phase 35a -- this screen is the only writer of billing locations, and every document form,
    // list filter and LOCATION cell in the app now reads them through one cached store. Without
    // this, a location added here would be invisible everywhere else until a full page reload.
    this.locationStore.invalidate(this.organizationId);

    this.startCreate();
    this.load();
  }

  private onSaveFailed(err: unknown): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save this location. Please try again.');
  }

  private load(): void {
    this.loading.set(true);
    this.organizationsService.listBillingLocations(this.organizationId, this.showInactive()).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load billing locations.');
      },
    });

    this.organizationsService.getBillingLocationSettings(this.organizationId).subscribe({
      next: (settings) => this.settings.set(settings),
      error: () => this.settings.set(null),
    });

    this.organizationsService.listWarehouses(this.organizationId).subscribe({
      next: (warehouses) => this.warehouses.set(warehouses),
      error: () => this.warehouses.set([]),
    });
  }
}
