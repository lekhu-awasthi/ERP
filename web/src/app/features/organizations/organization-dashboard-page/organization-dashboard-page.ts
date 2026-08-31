import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AuthService } from '../../../core/auth/auth.service';
import {
  MyOrganizations,
  Role,
  TenantFeatureKey,
  TenantSubscription,
} from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { DealList } from '../../crm/deal-list/deal-list';
import { TaskList } from '../../workflow/task-list/task-list';

/**
 * The Organization dashboard shell (roadmap Phase 1b task 11) -- replaces Phase 1a's generic
 * placeholder dashboard-page with an org-scoped one, including a company switcher (fine with
 * only one Organization to switch between for now) and the invite-user flow (task 12).
 *
 * Phase 14 (Role Reference): the invite form's role dropdown now sources ListRolesQuery (the two
 * shared system roles plus this Organization's own custom ones) instead of a hardcoded
 * Admin/Member union -- see InviteUserCommand's own doc comment for the backend side of this
 * change. Loading the role list can itself 403 for a Member without RoleView (see
 * ListRolesQuery's doc comment on that known, accepted interaction) -- handled the same way any
 * other load failure on this page is, via errorMessage.
 */
@Component({
  selector: 'app-organization-dashboard-page',
  imports: [ReactiveFormsModule, FormsModule, RouterLink, TaskList, DealList],
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
  protected readonly roles = signal<Role[]>([]);

  // Phase 20f (FR-2.6). The nav mirrors the API's own feature gate so a link never leads to a
  // screen that immediately 403s -- the same belt-and-braces split permission gating already uses
  // (conditional rendering here, independent enforcement in FeatureGateBehavior). The API stays
  // the authority: hiding a link is a courtesy, not the enforcement.
  protected readonly subscription = signal<TenantSubscription | null>(null);

  /** Defaults to false until the subscription loads, so a gated link never flashes in and out. */
  protected hasFeature(feature: TenantFeatureKey): boolean {
    return this.subscription()?.features.find((x) => x.feature === feature)?.isEnabled ?? false;
  }

  protected readonly organization = computed(() =>
    this.data()?.organizations.find((o) => o.organizationId === this.organizationId()) ?? null,
  );

  protected readonly inviteForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    roleId: ['', [Validators.required]],
  });
  protected readonly inviting = signal(false);
  protected readonly inviteMessage = signal<string | null>(null);
  protected readonly inviteError = signal<string | null>(null);

  constructor() {
    this.route.paramMap.subscribe((params) => {
      this.organizationId.set(params.get('id')!);
      this.inviteMessage.set(null);
      this.inviteError.set(null);
      this.loadRoles();
      this.loadSubscription();
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

    const { email, roleId } = this.inviteForm.getRawValue();

    this.organizationsService.inviteUser(this.organizationId(), { email, roleId }).subscribe({
      next: (result) => {
        this.inviting.set(false);
        this.inviteMessage.set(`Invitation sent to ${result.email}.`);
        this.inviteForm.reset({ email: '', roleId: this.defaultInviteRoleId() });
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

  private loadRoles(): void {
    this.organizationsService.listAllRoles(this.organizationId()).subscribe({
      next: (roles) => {
        this.roles.set(roles);
        this.inviteForm.patchValue({ roleId: this.defaultInviteRoleId() });
      },
      error: () => {
        // Non-fatal here -- a Member without RoleView simply sees an empty role dropdown and
        // can't invite anyway (they also lack OrganizationInviteUser); the invite panel itself is
        // already hidden from non-Admins by org.role === 'Admin' in the template.
        this.roles.set([]);
      },
    });
  }

  private loadSubscription(): void {
    this.organizationsService.getSubscription(this.organizationId()).subscribe({
      next: (subscription) => this.subscription.set(subscription),
      // Non-fatal, and deliberately fails closed: with no subscription loaded, hasFeature()
      // returns false and the feature-gated links stay hidden rather than leading to a 403.
      error: () => this.subscription.set(null),
    });
  }

  private defaultInviteRoleId(): string {
    return this.roles().find((r) => r.name === 'Member')?.id ?? this.roles()[0]?.id ?? '';
  }
}
