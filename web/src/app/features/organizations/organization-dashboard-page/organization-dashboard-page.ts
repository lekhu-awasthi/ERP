import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AuthService } from '../../../core/auth/auth.service';
import { MembershipRole, MyOrganizations } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { TaskList } from '../../workflow/task-list/task-list';

/**
 * The Organization dashboard shell (roadmap Phase 1b task 11) -- replaces Phase 1a's generic
 * placeholder dashboard-page with an org-scoped one, including a company switcher (fine with
 * only one Organization to switch between for now) and the invite-user flow (task 12).
 */
@Component({
  selector: 'app-organization-dashboard-page',
  imports: [ReactiveFormsModule, FormsModule, RouterLink, TaskList],
  templateUrl: './organization-dashboard-page.html',
})
export class OrganizationDashboardPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);

  protected readonly currentUser = this.authService.currentUser;
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly data = signal<MyOrganizations | null>(null);
  protected readonly organizationId = signal(this.route.snapshot.paramMap.get('id')!);

  protected readonly organization = computed(() =>
    this.data()?.organizations.find((o) => o.organizationId === this.organizationId()) ?? null,
  );

  protected readonly inviteForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    role: ['Member' as MembershipRole, [Validators.required]],
  });
  protected readonly inviting = signal(false);
  protected readonly inviteMessage = signal<string | null>(null);
  protected readonly inviteError = signal<string | null>(null);

  constructor() {
    this.route.paramMap.subscribe((params) => {
      this.organizationId.set(params.get('id')!);
      this.inviteMessage.set(null);
      this.inviteError.set(null);
    });
    this.load();
  }

  protected switchOrganization(organizationId: string): void {
    this.router.navigate(['/organizations', organizationId]);
  }

  protected invite(): void {
    if (this.inviteForm.invalid) {
      this.inviteForm.markAllAsTouched();
      return;
    }

    this.inviting.set(true);
    this.inviteMessage.set(null);
    this.inviteError.set(null);

    const { email, role } = this.inviteForm.getRawValue();

    this.organizationsService.inviteUser(this.organizationId(), { email, role }).subscribe({
      next: (result) => {
        this.inviting.set(false);
        this.inviteMessage.set(`Invitation sent to ${result.email}.`);
        this.inviteForm.reset({ email: '', role: 'Member' });
      },
      error: (err: unknown) => {
        this.inviting.set(false);
        this.inviteError.set(extractErrorMessage(err) ?? 'Could not send invitation. Please try again.');
      },
    });
  }

  protected logout(): void {
    this.authService.logout().subscribe(() => this.router.navigate(['/login']));
  }

  private load(): void {
    this.loading.set(true);
    this.organizationsService.myOrganizations().subscribe({
      next: (data) => {
        this.data.set(data);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load organization.');
      },
    });
  }
}
