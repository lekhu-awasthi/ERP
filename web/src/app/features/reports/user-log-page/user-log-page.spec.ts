import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { UserLogDto, UserLogRowDto } from '../../../core/reports/catalogue-reports.models';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { UserLogPage } from './user-log-page';

/**
 * Phase 26c. A failed attempt has to be visually distinct from a routine sign-in -- the whole point
 * of the report is spotting a run of them -- and a failed attempt that never resolved to a user must
 * still render, with its email standing in for the name it does not have.
 */
describe('UserLogPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function row(overrides: Partial<UserLogRowDto> = {}): UserLogRowDto {
    return {
      id: '33333333-3333-3333-3333-333333333333',
      userId: '22222222-2222-2222-2222-222222222222',
      fullName: 'Jane Doe',
      email: 'jane@example.com',
      occurredAt: '2026-05-03T09:15:32+00:00',
      deviceOs: 'Windows 10',
      ipAddress: '203.0.113.7',
      outcome: 'LoginSucceeded',
      description: 'Login Success',
      browser: 'Chrome 152.0.0.0',
      ...overrides,
    };
  }

  function page(report: Partial<UserLogDto> = {}) {
    const reports = {
      getUserLog: (): Observable<UserLogDto> =>
        of({
          fromDate: '2026-05-01',
          toDate: '2026-05-31',
          items: [row()],
          page: 1,
          pageSize: 50,
          totalCount: 1,
          ...report,
        }),
      exportUserLog: (): Observable<Blob> => of(new Blob()),
    };

    TestBed.configureTestingModule({
      imports: [UserLogPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CatalogueReportsService, useValue: reports },
        { provide: OrganizationsService, useValue: { listMembers: () => of([]) } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    const fixture = TestBed.createComponent(UserLogPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return { fixture, element, text: () => element.textContent ?? '' };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('shows the device, browser and address behind a sign-in', () => {
    const { text } = page();

    expect(text()).toContain('Jane Doe');
    expect(text()).toContain('Windows 10');
    expect(text()).toContain('Chrome 152.0.0.0');
    expect(text()).toContain('203.0.113.7');
    expect(text()).toContain('Login Success');
  });

  it('marks a failed attempt in danger colours so a run of them stands out', () => {
    const { element } = page({
      items: [row({ outcome: 'LoginFailed', description: 'Login Fail' })],
    });

    const badge = element.querySelector('tbody tr .badge') as HTMLElement;
    expect(badge.textContent?.trim()).toBe('Login Fail');
    expect(badge.className).toContain('text-danger');
  });

  it('renders a failed attempt that never resolved to a user, with the email standing in for the name', () => {
    const { text } = page({
      items: [
        row({
          userId: null,
          fullName: 'jane@example.com',
          outcome: 'LoginFailed',
          description: 'Login Fail',
        }),
      ],
    });

    expect(text()).toContain('jane@example.com');
    expect(text()).toContain('Login Fail');
  });

  it('shows an empty state rather than a blank table', () => {
    const { text } = page({ items: [], totalCount: 0 });

    expect(text()).toContain('No sign-in activity in this period');
  });
});
