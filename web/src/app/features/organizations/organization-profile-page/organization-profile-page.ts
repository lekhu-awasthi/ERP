import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { BackfillLocationsResult, OrganizationProfile } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 39 — `Configurations > Organization Profile`: the organization's own details, and the logo
 * that prints at the top of every document it issues.
 *
 * <b>The logo is the feature; the details are context.</b> The roadmap's item is the phase-1b wizard
 * gap — the reference product's wizard step 1 takes a Company Logo and this codebase never built the
 * field. Showing it on a page of nothing but an upload box would be a screen nobody could place, so
 * it sits beside the fields the printed header prints next to it.
 *
 * <b>Phase 43 made those fields editable.</b> Phase 39 shipped them read-only and said so on the
 * page; `Organization` had been create-only since phase 1b, so eight fields printed on every
 * customer-facing PDF had no command that wrote them — phase-31's rule failing in its pure form.
 * `workspaceName` stays a read-only line: it is the slug that addresses the tenant, the server's
 * request record does not carry it, and showing it as a disabled input would imply otherwise.
 *
 * <b>This screen is an addition, and that is recorded rather than assumed.</b> The live pass
 * (2026-09-13) found the reference product's own Overview tab has no logo control at all — its EDIT
 * DETAILS dialog carries the nine text fields and nothing else — so there its logo can only ever be
 * set during signup. An ERP whose logo can never be corrected afterwards is a worse product, so the
 * divergence is deliberate; see docs/phase-39-status.md, Decision D.
 *
 * <b>Why a Blob and an object url</b> rather than pointing `<img src>` at the endpoint: the logo
 * route is authenticated like every other read here, and an `<img>` element does not send the auth
 * cookie cross-origin. Fetching through `HttpClient` (which does) and binding the object url is the
 * same reasoning phase 22 applied to its `<iframe [src]>`, reached from the other direction — and it
 * needs no `DomSanitizer` bypass, because an object url on an `<img>` is not a sanitised context.
 */
@Component({
  selector: 'app-organization-profile-page',
  imports: [ReactiveFormsModule, BsDateInput, StatusBanner],
  templateUrl: './organization-profile-page.html',
})
export class OrganizationProfilePage implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly profile = signal<OrganizationProfile | null>(null);
  protected readonly logoUrl = signal<string | null>(null);

  /** Phase 43 — separate from `saving`, which the logo upload owns: the two controls sit in two
   * cards and either can be busy while the other is not. */
  protected readonly savingDetails = signal(false);

  /**
   * Phase 44 (35a carried item #5, 35b #6) -- the one-off billing-location backfill.
   *
   * It lives on this screen rather than getting one of its own because phase 31's rule is that a
   * tenant-level write is only reachable if you can name both the command and the screen that calls
   * it, and this is already the Admin-only page about the organization itself.
   */
  protected readonly backfilling = signal(false);

  protected readonly backfillResult = signal<BackfillLocationsResult | null>(null);

  /** The app is zoneless, so the date the BS control drives lives in its own signal rather than
   * being read off a FormControl inside a computed (phase 17). */
  protected readonly accountingStartDate = signal('');

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    industry: ['', [Validators.required, Validators.maxLength(100)]],
    address: ['', Validators.maxLength(500)],
    email: ['', [Validators.email, Validators.maxLength(256)]],
    phone: ['', Validators.maxLength(20)],
    panNumber: ['', Validators.maxLength(50)],
    website: ['', Validators.maxLength(256)],
    isVatRegistered: [false],
  });

  /** The reference product's own rules, restated where the user can read them before choosing a
   * file rather than after the server rejects one. */
  protected readonly rules = 'PNG, JPEG or GIF · at least 300 × 300 pixels · up to 5 MB';

  constructor() {
    this.load();
  }

  /** Object urls are a leak if nobody revokes them, and this page mints a new one per upload. */
  ngOnDestroy(): void {
    this.releaseLogoUrl();
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.organizationsService.uploadLogo(this.organizationId, file).subscribe({
      next: () => {
        this.saving.set(false);
        this.successMessage.set('Logo updated. It will appear on documents printed from now on.');
        // Cleared so re-picking the same file after a failure still fires a change event.
        input.value = '';
        this.loadLogo();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        input.value = '';
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  protected remove(): void {
    if (!window.confirm('Remove the organization logo? Documents will print without it.')) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.organizationsService.removeLogo(this.organizationId).subscribe({
      next: () => {
        this.saving.set(false);
        this.successMessage.set('Logo removed.');
        this.releaseLogoUrl();
        this.profile.update((x) => (x ? { ...x, hasLogo: false } : x));
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  protected onBackfillLocations(): void {
    this.backfilling.set(true);
    this.backfillResult.set(null);
    this.errorMessage.set(null);

    this.organizationsService.backfillLocations(this.organizationId).subscribe({
      next: (result) => {
        this.backfillResult.set(result);
        this.backfilling.set(false);
      },
      error: (err: unknown) => {
        this.backfilling.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not backfill billing locations.');
      },
    });
  }

  protected saveDetails(): void {
    if (this.form.invalid || !this.accountingStartDate()) {
      this.form.markAllAsTouched();
      this.errorMessage.set('Fill in the required details before saving.');
      return;
    }

    const value = this.form.getRawValue();
    this.savingDetails.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.organizationsService
      .updateProfile(this.organizationId, {
        name: value.name.trim(),
        industry: value.industry.trim(),
        address: value.address.trim() || null,
        accountingStartDate: this.accountingStartDate(),
        isVatRegistered: value.isVatRegistered,
        email: value.email.trim() || null,
        phone: value.phone.trim() || null,
        panNumber: value.panNumber.trim() || null,
        website: value.website.trim() || null,
      })
      .subscribe({
        next: () => {
          this.savingDetails.set(false);
          this.successMessage.set('Organization details saved.');
          // Re-read rather than patching the signal: the server trims and the header of every
          // later print reads what it stored, not what was typed.
          this.load();
        },
        error: (error: unknown) => {
          this.savingDetails.set(false);
          this.errorMessage.set(extractErrorMessage(error));
        },
      });
  }

  private load(): void {
    this.loading.set(true);

    this.organizationsService.getProfile(this.organizationId).subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.form.setValue({
          name: profile.name,
          industry: profile.industry,
          address: profile.address ?? '',
          email: profile.email ?? '',
          phone: profile.phone ?? '',
          panNumber: profile.panNumber ?? '',
          website: profile.website ?? '',
          isVatRegistered: profile.isVatRegistered,
        });
        this.accountingStartDate.set(profile.accountingStartDate.slice(0, 10));
        this.loading.set(false);

        if (profile.hasLogo) {
          this.loadLogo();
        }
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }

  private loadLogo(): void {
    this.organizationsService.getLogo(this.organizationId).subscribe({
      next: (blob) => {
        this.releaseLogoUrl();
        this.logoUrl.set(URL.createObjectURL(blob));
        this.profile.update((x) => (x ? { ...x, hasLogo: true } : x));
      },
      // Non-fatal: the page is still readable without the picture, and a 404 here simply means the
      // organization has no logo yet.
      error: () => this.releaseLogoUrl(),
    });
  }

  private releaseLogoUrl(): void {
    const url = this.logoUrl();

    if (url) {
      URL.revokeObjectURL(url);
      this.logoUrl.set(null);
    }
  }
}
