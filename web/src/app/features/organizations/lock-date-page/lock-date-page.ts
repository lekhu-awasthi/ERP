import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Admin-only view/set/clear of Organization.LockDate (roadmap Phase 16a, NFR-3.4) -- the seam
 * schema'd since Phase 1b, enforced nowhere until this phase's LockDateBehavior. Mirrors
 * AccountingDefaultsPage's shape: load current value, edit, Save, with an explicit Clear action
 * since a lock date is meaningfully different from "not set" (an empty date input alone can't
 * distinguish the two on save).
 */
@Component({
  selector: 'app-lock-date-page',
  imports: [RouterLink, BsDateInput, NepaliDatePipe],
  templateUrl: './lock-date-page.html',
})
export class LockDatePage {
  private readonly route = inject(ActivatedRoute);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly currentLockDate = signal<string | null>(null);
  protected readonly lockDate = signal<string>('');

  constructor() {
    this.load();
  }

  protected save(): void {
    this.saving.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.organizationsService.setLockDate(this.organizationId, this.lockDate() || null).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.currentLockDate.set(result.lockDate);
        this.successMessage.set(result.lockDate ? `Lock date set to ${result.lockDate}.` : 'Lock date cleared.');
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save lock date. Please try again.');
      },
    });
  }

  protected clear(): void {
    this.lockDate.set('');
    this.save();
  }

  private load(): void {
    this.loading.set(true);

    this.organizationsService.getLockDate(this.organizationId).subscribe({
      next: (result) => {
        this.currentLockDate.set(result.lockDate);
        this.lockDate.set(result.lockDate ?? '');
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load lock date.');
      },
    });
  }
}
