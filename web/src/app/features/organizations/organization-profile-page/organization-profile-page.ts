import { Component, OnDestroy, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { OrganizationProfile } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Phase 39 — `Configurations > Organization Profile`: the organization's own details, and the logo
 * that prints at the top of every document it issues.
 *
 * <b>The logo is the feature; the details are context.</b> The roadmap's item is the phase-1b wizard
 * gap — the reference product's wizard step 1 takes a Company Logo and this codebase never built the
 * field. Showing it on a page of nothing but an upload box would be a screen nobody could place, so
 * it sits beside the nine fields the printed header prints next to it. Editing those nine is a
 * carried item and the page says so rather than offering controls that do not work.
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
  imports: [NepaliDatePipe],
  templateUrl: './organization-profile-page.html',
})
export class OrganizationProfilePage implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly profile = signal<OrganizationProfile | null>(null);
  protected readonly logoUrl = signal<string | null>(null);

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

  private load(): void {
    this.loading.set(true);

    this.organizationsService.getProfile(this.organizationId).subscribe({
      next: (profile) => {
        this.profile.set(profile);
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
