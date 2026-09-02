import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import {
  TransactionApprovalDocumentType,
  TransactionApprovalRowDto,
} from '../../../core/workflow/workflow.models';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { SystemAuditReportPage } from '../../reports/system-audit-report-page/system-audit-report-page';
import { TransactionApprovalQueuePage } from './transaction-approval-queue-page';

/**
 * Phase 23, item 3. Both of these screens shipped a `detailRoute` switch that returned <b>null</b>
 * for SalesOrder, with a comment saying no Angular detail page existed for it. Phase 18 built and
 * routed that page as a mid-phase scope expansion and never came back to these two call sites, so
 * for four phases a SalesOrder row rendered with no Open link while the page it needed was live.
 *
 * The roadmap's own Phase 23 exit criterion names it ("its queue row links correctly"), and the
 * testing bar's note is the point of this file: <b>a component test would have caught it</b>. So
 * rather than asserting the one type that was broken, both switches are now driven over every
 * member of their own document-type union -- which is what stops the next type added in Phase 24+
 * from quietly falling through to null.
 */
const ALL_TYPES: readonly TransactionApprovalDocumentType[] = [
  'Quotation',
  'SalesOrder',
  'Invoice',
  'CreditNote',
  'PurchaseOrder',
  'PurchaseBill',
  'Expense',
  'DebitNote',
  'JournalVoucher',
  'CashTransfer',
  'WarehouseTransfer',
  'InventoryAdjustment',
  'Payment',
];

const organizationId = '11111111-1111-1111-1111-111111111111';
const documentId = '22222222-2222-2222-2222-222222222222';

function row(
  documentType: TransactionApprovalDocumentType,
  direction: 'Paid' | 'Received' | null = null,
): TransactionApprovalRowDto {
  return {
    documentType,
    documentId,
    code: 'DRAFT',
    date: '2026-09-01',
    createdAt: '2026-09-01T00:00:00Z',
    contactId: null,
    contactName: null,
    reference: null,
    direction,
  };
}

describe('detailRoute across both screens that link into a document', () => {
  function build<T>(type: Type<T>): T {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          // Both pages read the organization id off the route, so the asserted routes below are
          // the real ones a user would navigate to rather than ones containing a null segment.
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({ id: organizationId }) },
            paramMap: of(convertToParamMap({ id: organizationId })),
          },
        },
        {
          provide: WorkflowService,
          useValue: {
            getTransactionApprovalQueue: () => of({ rows: [] }),
            getSystemAuditReport: () =>
              of({ items: [], page: 1, pageSize: 25, totalCount: 0 }),
          },
        },
      ],
    });
    return TestBed.createComponent(type).componentInstance;
  }

  describe('TransactionApprovalQueuePage', () => {
    it('resolves a real route for every document type it can display', () => {
      const page = build(TransactionApprovalQueuePage) as unknown as {
        detailRoute(r: TransactionApprovalRowDto): string[] | null;
      };

      for (const type of ALL_TYPES) {
        const route = page.detailRoute(row(type, type === 'Payment' ? 'Received' : null));
        expect(route, `${type} must resolve to a route`).not.toBeNull();
        expect(route!.length, `${type} route looks empty`).toBeGreaterThan(2);
        expect(route![route!.length - 1], `${type} route must end at the document`).toBe(documentId);
      }
    });

    it('routes SalesOrder at the page Phase 18 actually built', () => {
      const page = build(TransactionApprovalQueuePage) as unknown as {
        detailRoute(r: TransactionApprovalRowDto): string[] | null;
      };

      expect(page.detailRoute(row('SalesOrder'))).toEqual([
        '/organizations',
        organizationId,
        'sales',
        'sales-orders',
        documentId,
      ]);
    });

    it('still splits Payment by direction, which shares one aggregate across two pages', () => {
      const page = build(TransactionApprovalQueuePage) as unknown as {
        detailRoute(r: TransactionApprovalRowDto): string[] | null;
      };

      expect(page.detailRoute(row('Payment', 'Paid'))).toContain('supplier-payments');
      expect(page.detailRoute(row('Payment', 'Received'))).not.toContain('supplier-payments');
    });
  });

  describe('SystemAuditReportPage', () => {
    it('resolves a real route for every transactional document type', () => {
      const page = build(SystemAuditReportPage) as unknown as {
        detailRoute(r: { documentType: string; documentId: string; direction: string | null }): string[] | null;
      };

      for (const type of ALL_TYPES) {
        const route = page.detailRoute({
          documentType: type,
          documentId,
          direction: type === 'Payment' ? 'Received' : null,
        });
        expect(route, `${type} must resolve to a route`).not.toBeNull();
        expect(route![route!.length - 1], `${type} route must end at the document`).toBe(documentId);
      }
    });

    it('routes SalesOrder, which returned null here too', () => {
      const page = build(SystemAuditReportPage) as unknown as {
        detailRoute(r: { documentType: string; documentId: string; direction: string | null }): string[] | null;
      };

      expect(page.detailRoute({ documentType: 'SalesOrder', documentId, direction: null })).toContain(
        'sales-orders',
      );
    });

    it('returns null only for DocumentExtraction, which is an action rather than a document', () => {
      // Phase 22's AI extraction audits under a document type that has no detail page at all --
      // the one legitimate null, and worth pinning so it is not "fixed" by mistake.
      const page = build(SystemAuditReportPage) as unknown as {
        detailRoute(r: { documentType: string; documentId: string; direction: string | null }): string[] | null;
      };

      expect(
        page.detailRoute({ documentType: 'DocumentExtraction', documentId, direction: null }),
      ).toBeNull();
    });
  });
});
