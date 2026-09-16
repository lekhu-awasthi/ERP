import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { MyOrganizations, OrganizationSummary } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { OrganizationListPage } from './organization-list-page';

/**
 * Phase 49 — the screen where this product's divergence from the reference one is visible.
 *
 * <p>Read live on 2026-09-16, the reference product answers the equivalent request for an expired
 * trial with an empty list: the organization is gone and the owner is shown first-run onboarding.
 * This product keeps the row and marks it (docs/phase-49-status.md Decision A), so the two things
 * worth pinning are that an expired organization is <b>still listed</b> and that it <b>says so</b>
 * — a row that were present but silent would be the worst of both choices.</p>
 */
describe('OrganizationListPage', () => {
  let fixture: ComponentFixture<OrganizationListPage>;

  const baseOrganization: OrganizationSummary = {
    organizationId: 'org-1',
    name: 'Acme Traders',
    workspaceName: 'acme',
    industry: 'Retail',
    role: 'Admin',
    termEndsAt: '2027-01-01T23:59:59Z',
    isExpired: false,
  };

  async function render(organizations: OrganizationSummary[]): Promise<void> {
    const data: MyOrganizations = { organizations, requests: [], invitations: [] };

    await TestBed.configureTestingModule({
      imports: [OrganizationListPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        {
          provide: OrganizationsService,
          useValue: { myOrganizations: () => of(data), acceptInvitation: () => of({}) },
        },
        {
          provide: AuthService,
          useValue: { currentUser: () => ({ fullName: 'Jane Doe', email: 'jane@example.com' }), logout: () => of({}) },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(OrganizationListPage);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  it('keeps an expired organization in the list and marks it read-only', async () => {
    await render([{ ...baseOrganization, isExpired: true, termEndsAt: '2026-09-01T23:59:59Z' }]);

    expect(text()).toContain('Acme Traders');
    expect(text()).toContain('Expired');
    expect(text()).toContain('read-only');
    // Still a way in: read-only has to mean readable, so the row stays a link.
    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/organizations/org-1"]')).toBeTruthy();
  });

  it('shows a live organization its term end and no expiry mark', async () => {
    await render([baseOrganization]);

    expect(text()).toContain('Until');
    expect(text()).not.toContain('read-only');
  });

  /** A tenant with no subscription row at all — a fixture, or a partially migrated tenant — is
   *  reported live by the server, and this screen must not invent a date for it either. */
  it('says nothing about a term for an organization that has no subscription row', async () => {
    await render([{ ...baseOrganization, termEndsAt: null }]);

    expect(text()).toContain('Acme Traders');
    expect(text()).not.toContain('Until');
    expect(text()).not.toContain('read-only');
  });
});
