import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { EMPTY, catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { OrganizationsService } from '../../../core/organizations/organizations.service';

type WizardStep = 1 | 2 | 3;
type WorkspaceStatus = 'idle' | 'checking' | 'available' | 'taken';

/** ~30 common Nepali SME sectors -- free text, this just seeds suggestions. */
const INDUSTRY_SUGGESTIONS = [
  'Agriculture', 'Automobile', 'Banking & Finance', 'Construction', 'Consulting',
  'Education', 'Electronics', 'Event Management', 'Fashion & Apparel', 'Food & Beverage',
  'Handicrafts', 'Healthcare', 'Hospitality & Tourism', 'Import & Export', 'Information Technology',
  'Insurance', 'Logistics & Transportation', 'Manufacturing', 'Media & Entertainment', 'NGO / INGO',
  'Pharmaceuticals', 'Real Estate', 'Retail & Trading', 'Telecommunications', 'Textiles',
  'Wholesale', 'Other',
];

/**
 * The 3-step New Organization wizard (erp-module-scan.md's Signup & Onboarding section) --
 * client-side pagination over one form, submitted as a single CreateOrganizationCommand
 * (roadmap Phase 1b task 7) rather than three separate commands per step.
 */
@Component({
  selector: 'app-new-organization-wizard',
  imports: [ReactiveFormsModule],
  templateUrl: './new-organization-wizard.html',
})
export class NewOrganizationWizard {
  private readonly fb = inject(FormBuilder);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly router = inject(Router);

  protected readonly industrySuggestions = INDUSTRY_SUGGESTIONS;
  protected readonly step = signal<WizardStep>(1);
  protected readonly workspaceStatus = signal<WorkspaceStatus>('idle');
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    industry: ['', [Validators.required, Validators.maxLength(100)]],
    address: [''],
    accountingStartDate: ['', [Validators.required]],
    isVatRegistered: [false],
    workspaceName: ['', [Validators.required, Validators.pattern(/^[a-zA-Z0-9][a-zA-Z0-9-]*$/)]],
    email: ['', [Validators.email]],
    phone: [''],
    panNumber: [''],
    website: [''],
    trackInventory: [false],
    multipleLocations: [false],
    multipleWarehouses: [false],
    multiCurrency: [false],
    manufacturing: [false],
    posRetail: [false],
    posRestaurant: [false],
  });

  private readonly step1Controls = ['name', 'industry', 'accountingStartDate', 'workspaceName', 'email'] as const;

  constructor() {
    this.form.controls.workspaceName.valueChanges
      .pipe(
        debounceTime(400),
        distinctUntilChanged(),
        switchMap((value) => {
          if (this.form.controls.workspaceName.invalid || !value) {
            this.workspaceStatus.set('idle');
            return EMPTY;
          }

          this.workspaceStatus.set('checking');
          return this.organizationsService.checkWorkspaceNameAvailability(value).pipe(catchError(() => of(null)));
        }),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        if (result) {
          this.workspaceStatus.set(result.isAvailable ? 'available' : 'taken');
        }
      });
  }

  protected goToStep2(): void {
    this.step1Controls.forEach((name) => this.form.controls[name].markAsTouched());

    const step1Valid = this.step1Controls.every((name) => this.form.controls[name].valid);

    if (!step1Valid || this.workspaceStatus() !== 'available') {
      return;
    }

    this.step.set(2);
  }

  protected goToStep3(): void {
    this.step.set(3);
  }

  protected back(): void {
    this.step.update((current) => (current > 1 ? ((current - 1) as WizardStep) : current));
  }

  protected submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    this.organizationsService
      .createOrganization({
        name: value.name,
        industry: value.industry,
        address: value.address || null,
        accountingStartDate: value.accountingStartDate,
        isVatRegistered: value.isVatRegistered,
        workspaceName: value.workspaceName,
        email: value.email || null,
        phone: value.phone || null,
        panNumber: value.panNumber || null,
        website: value.website || null,
        trackInventory: value.trackInventory,
        multipleLocations: value.multipleLocations,
        multipleWarehouses: value.multipleWarehouses,
        multiCurrency: value.multiCurrency,
        manufacturing: value.manufacturing,
        posRetail: value.posRetail,
        posRestaurant: value.posRestaurant,
      })
      .subscribe({
        next: (result) =>
          this.router.navigate(['/organizations', result.organizationId, 'welcome'], {
            queryParams: { name: result.name },
          }),
        error: (err: unknown) => {
          this.submitting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not create organization. Please try again.');
        },
      });
  }
}
