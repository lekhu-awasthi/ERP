import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { AuditRowDto } from '../../../core/workflow/workflow.models';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { DatePreferenceService } from '../../../shared/formatting/date-preference';
import { SystemAuditReportPage } from './system-audit-report-page';

/**
 * Phase 48, closing phase 44's carried item #6 for this screen — and pinning phase 48's own change
 * to it.
 *
 * <p>Phase 44 stamped a <b>location</b> on every audit row, with the deliberate rule that the column
 * records <i>where the document was when the action happened</i> and is therefore <b>null</b> for a
 * row written before phase 44, for a record parent, and for any type outside the tenant's location
 * scope. A blank there is correct and must not become a fabricated "HeadOffice".</p>
 *
 * <p>Phase 48 additionally moved this report's timestamp off Angular's `DatePipe`. It had been
 * rendering `yyyy-MM-dd HH:mm:ss` in the browser's Gregorian locale while the date <i>filters</i> on
 * the same screen honoured the BS toggle — one screen disagreeing with itself about the calendar.</p>
 */
describe('SystemAuditReportPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function row(overrides: Partial<AuditRowDto> = {}): AuditRowDto {
    return {
      id: '44444444-4444-4444-4444-444444444444',
      // 2026-05-04 20:30 UTC is 2026-05-05 02:15 on the Nepal wall clock -- the boundary case.
      createdAt: '2026-05-04T20:30:00.0000000+00:00',
      userId: '55555555-5555-5555-5555-555555555555',
      userName: 'Asha Sharma',
      action: 'Approve',
      documentType: 'Invoice',
      documentId: '66666666-6666-6666-6666-666666666666',
      direction: null,
      location: 'HeadOffice',
      ...overrides,
    };
  }

  function page(items: AuditRowDto[] = [row()], bs = false) {
    const workflow = {
      getSystemAuditReport: (): Observable<unknown> =>
        of({ items, page: 1, pageSize: 50, totalCount: items.length }),
      exportSystemAuditReport: (): Observable<Blob> => of(new Blob()),
    };

    TestBed.configureTestingModule({
      imports: [SystemAuditReportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: WorkflowService, useValue: workflow },
        {
          provide: OrganizationsService,
          useValue: {
            listMembers: () => of([]),
            listWarehouses: () => of([]),
            listBillingLocations: () => of([]),
            getBillingLocationSettings: () =>
              of({
                locationScopeMode: 'SalesTransactionsOnly',
                locationWiseReportPermission: false,
                multipleLocationsEnabled: false,
                locationBearingDocumentTypes: [],
              }),
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    // The calendar the report is read in. The pipe is impure precisely so this can change under it.
    TestBed.inject(DatePreferenceService).set(bs ? 'BS' : 'AD');

    const fixture = TestBed.createComponent(SystemAuditReportPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return { fixture, element, text: () => element.textContent ?? '' };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('keeps the timestamp to the second, which is what an audit trail is for', () => {
    const { text } = page();

    expect(text()).toContain('02:15:00');
  });

  it('dates a row by the Nepal wall clock, not by UTC', () => {
    // 20:30 UTC on the 4th is already the 5th in Kathmandu. Taking slice(0, 10) of the instant --
    // what this screen did before phase 48 -- dates every late-evening action to the day before.
    const { text } = page();

    expect(text(), 'the row was dated by its UTC day').toContain('05-05-2026');
    expect(text()).not.toContain('04-05-2026');
  });

  it('renders that same instant in BS when the tenant reads in BS', () => {
    // The point of the change: before phase 48 this cell was Gregorian whatever the toggle said,
    // while the date filters beside it were not.
    const { text } = page([row()], true);

    // AD 2026-05-05 is BS 2083-01-22.
    expect(text()).toContain('22-01-2083');
    expect(text(), 'the BS reading still carries the seconds').toContain('02:15:00');
  });

  it('shows the location the action happened at', () => {
    const { text } = page();

    expect(text()).toContain('HeadOffice');
  });

  it('leaves the location blank rather than inventing one, where the row carries none', () => {
    // Phase 44's rule: a row written before that phase, a record parent, or a type outside the
    // tenant's location scope all legitimately carry null, and a stamped audit column is never
    // backfilled.
    const { element } = page([row({ location: null, documentType: 'Invoice' })]);

    const cells = Array.from(element.querySelectorAll('tbody tr td')).map((c) => c.textContent?.trim());
    expect(cells, 'a null location was rendered as a real one').not.toContain('HeadOffice');
  });
});
