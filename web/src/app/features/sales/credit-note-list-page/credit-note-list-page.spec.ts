import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { CreditNoteListItem } from '../../../core/sales/sales.models';
import { SalesService } from '../../../core/sales/sales.service';
import { organizationsStub, visibleText } from '../../../core/pos/pos-reports.testing';
import { CreditNoteListPage } from './credit-note-list-page';

/**
 * Phase 86 -- a credit note's card names the warehouse its goods went back into, as the note's page does, and only when
 * the server says stock moved (a till refund of goods, here or at another branch; nothing for services or a draft).
 */
describe('CreditNoteListPage', () => {
  const row = (overrides: Partial<CreditNoteListItem>): CreditNoteListItem => ({
    id: 'cn-1', organizationId: 'org-1', contactId: 'walk-in', code: '0001', date: '2026-10-12', reference: '0001',
    status: 'Approved', approvedByUserId: 'u1', approvedAt: '2026-10-12T03:00:00Z', createdAt: '2026-10-12T03:00:00Z',
    referrerType: 'Invoice', referrerId: 'inv-1', discountPct: 0, terms: null, locationId: null, againstInvoiceId: null,
    againstInvoiceNumber: null, againstInvoiceDate: null, reason: 'Parcel came back cold', returnWarehouseName: null,
    ...overrides,
  });

  function page(items: CreditNoteListItem[]) {
    TestBed.configureTestingModule({
      imports: [CreditNoteListPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        organizationsStub,
        {
          provide: SalesService,
          useValue: { listCreditNotes: () => of({ page: 1, pageSize: 50, totalCount: items.length, items }) },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'org-1' } } } },
      ],
    });

    const fixture = TestBed.createComponent(CreditNoteListPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    return { element, text: () => visibleText(element) };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('names the warehouse each refund’s goods went back into, and none where nothing moved', () => {
    const p = page([
      row({ id: 'cn-away', code: '0001', returnWarehouseName: 'Lakeside Store' }),
      row({ id: 'cn-momo', code: '0002', returnWarehouseName: null }),
    ]);

    const cards = [...p.element.querySelectorAll('a.list-group-item')].map((a) => a.textContent?.replace(/\s+/g, ' ').trim());
    expect(cards[0]).toContain('Goods returned into Lakeside Store');
    expect(cards[1]).not.toContain('Goods returned into');
    expect(p.text().match(/Goods returned into/g)?.length).toBe(1);
  });
});
