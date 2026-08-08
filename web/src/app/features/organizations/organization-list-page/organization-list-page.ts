import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AuthService } from '../../../core/auth/auth.service';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { MyOrganizations } from '../../../core/organizations/organizations.models';

type Tab = 'organizations' | 'requests' | 'invitations';

/** Post-login landing page (PRD FR-1.3): the "Your Organizations / Requests / Invitations" 3-tab view. */
@Component({
  selector: 'app-organization-list-page',
  imports: [RouterLink],
  templateUrl: './organization-list-page.html',
})
export class OrganizationListPage {
  private readonly organizationsService = inject(OrganizationsService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly currentUser = this.authService.currentUser;
  protected readonly activeTab = signal<Tab>('organizations');
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly data = signal<MyOrganizations | null>(null);
  protected readonly acceptingId = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected setTab(tab: Tab): void {
    this.activeTab.set(tab);
  }

  protected openOrganization(organizationId: string): void {
    this.router.navigate(['/organizations', organizationId]);
  }

  protected acceptInvitation(membershipId: string): void {
    this.acceptingId.set(membershipId);
    this.organizationsService.acceptInvitation(membershipId).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.acceptingId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not accept invitation. Please try again.');
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
        this.acceptingId.set(null);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.acceptingId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load your organizations.');
      },
    });
  }
}
