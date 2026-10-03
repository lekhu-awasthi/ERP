import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { PosReportsService } from '../../../core/pos/pos-reports.service';
import { organizationsStub, visibleText } from '../../../core/pos/pos-reports.testing';
import { PosSessionListPage } from './pos-session-list-page';

/**
 * Phase 66 -- the sessions list phase 61 left for this phase: a row per drawer, opening its X/Z page,
 * with what it took and whether its count came up short; searchable by session or cashier.
 */
describe('PosSessionListPage', () => {
  function page() {
    const searches: string[] = [];
    TestBed.configureTestingModule({
      imports: [PosSessionListPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        organizationsStub,
        {
          provide: PosReportsService,
          useValue: {
            listSessions: (_o: string, _p: unknown, _s: unknown, search: string) => {
              searches.push(search);
              return of({
                page: 1, pageSize: 50, totalCount: 1,
                items: [{
                  id: 'ses-1', code: 'SES0001', locationId: 'loc-1', locationName: 'Thamel', userId: 'u1', userName: 'Sita',
                  status: 'Closed', openedAt: '2026-10-02T03:00:00Z', closedAt: '2026-10-02T12:00:00Z', openingFloat: 1000,
                  salesCount: 3, sales: 1198, refunds: 249, expectedCash: 1684, countedCash: 1679, cashDifference: -5,
                }],
              });
            },
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'org-1' } } } },
      ],
    });

    const fixture = TestBed.createComponent(PosSessionListPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, element, searches, text: () => visibleText(element) };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('opens a session page from its row and shows what it took and its short count', () => {
    const p = page();
    const link = [...p.element.querySelectorAll('a')].find((a) => a.textContent?.trim() === 'SES0001');
    expect(link?.getAttribute('href')).toBe('/organizations/org-1/pos/sessions/ses-1');
    expect(p.text()).toContain('1,198.00 3 sale(s) 249.00 1,684.00 1,679.00 -5.00');
  });

  it('marks a short count in the danger colour', () => {
    const p = page();
    const cells = [...p.element.querySelectorAll('td.text-danger')];
    expect(cells.map((c) => c.textContent?.trim())).toEqual(['-5.00']);
  });
});
