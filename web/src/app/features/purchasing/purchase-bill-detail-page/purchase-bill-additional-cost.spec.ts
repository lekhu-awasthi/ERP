import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { PrintingService } from '../../../core/printing/printing.service';
import { PurchaseBillDetail } from '../../../core/purchasing/purchasing.models';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { InboxService } from '../../../core/workflow/inbox.service';
import { PurchaseBillDetailPage } from './purchase-bill-detail-page';

/**
 * Phase 29 (FR-6.15). Phase-23 bug #1's discipline again: a DTO can carry the whole Additional Cost
 * section and its allocations while the template has no element for any of it, and every other check
 * stays green. So these assert the figures actually reach the screen -- and that the matrix is the
 * product-by-cost-term shape the reference product renders, not a flat list.
 */
describe('PurchaseBillDetailPage — Additional Cost', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const billId = '22222222-2222-2222-2222-222222222222';

  const products = [
    { id: 'p-bike', code: 'P0001', name: 'Motorbike', type: 'Goods' },
    { id: 'p-helmet', code: 'P0002', name: 'Helmet', type: 'Goods' },
    { id: 'p-consult', code: 'P0003', name: 'Consulting', type: 'Service' },
  ];

  const costTerms = [
    { id: 'ct-freight', organizationId, name: 'Freight', category: 'AdditionalCost', isActive: true },
    { id: 'ct-duty', organizationId, name: 'Custom Duty', category: 'AdditionalCost', isActive: true },
    { id: 'ct-old', organizationId, name: 'Retired Charge', category: 'AdditionalCost', isActive: false },
    { id: 'ct-labour', organizationId, name: 'Labour', category: 'ProductionCost', isActive: true },
  ];

  function detail(overrides: Partial<PurchaseBillDetail> = {}): PurchaseBillDetail {
    return {
      id: billId,
      organizationId,
      contactId: 'c-1',
      warehouseId: 'w-1',
      code: 'PB0001',
      date: '2026-01-10',
      reference: null,
      supplierInvoiceReference: null,
      isImport: false,
      importCountry: null,
      importDate: null,
      importDocumentNo: null,
      tdsTypeId: null,
      tdsAmount: 0,
      status: 'Approved',
      approvedByUserId: null,
      approvedAt: '2026-01-10T05:00:00Z',
      createdAt: '2026-01-10T04:00:00Z',
      referrerType: null,
      referrerId: null,
      discountPct: 0,
      currencyCode: 'NPR',
      exchangeRate: 1,
      grandTotal: 6600,
      lines: [
        {
          id: 'l-bike',
          productId: 'p-bike',
          quantity: 10,
          rate: 600,
          vatRate: 'NoVat',
          expenditureClassification: 'Others',
          discountPct: 0,
          amount: 6000,
          vatAmount: 0,
        },
        {
          id: 'l-helmet',
          productId: 'p-helmet',
          quantity: 5,
          rate: 120,
          vatRate: 'NoVat',
          expenditureClassification: 'Others',
          discountPct: 0,
          amount: 600,
          vatAmount: 0,
        },
      ],
      glLines: null,
      additionalCosts: [
        {
          id: 'ac-1',
          costTermId: 'ct-freight',
          productId: null,
          method: 'Value',
          amount: 660,
          allocations: [
            { purchaseBillLineId: 'l-bike', amount: 600 },
            { purchaseBillLineId: 'l-helmet', amount: 60 },
          ],
        },
      ],
      isProductWiseAdditionalCost: false,
      additionalCostTotal: 660,
      capitalisedAdditionalCost: 660,
      additionalCostRoundingAdjustment: 0,
      ...overrides,
    };
  }

  function page(bill: PurchaseBillDetail = detail(), routeBillId: string = billId) {
    const purchasingService = {
      getPurchaseBill: (): Observable<PurchaseBillDetail> => of(bill),
    };

    TestBed.configureTestingModule({
      imports: [PurchaseBillDetailPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PurchasingService, useValue: purchasingService },
        { provide: ContactsService, useValue: { listAllContacts: () => of([]) } },
        { provide: CatalogService, useValue: { listAllProducts: () => of(products) } },
        { provide: AccountingService, useValue: { listAllAccounts: () => of([]) } },
        {
          provide: OrganizationsService,
          useValue: {
            listWarehouses: () => of([]),
            // Phase 28's shared currency/rate control reads the tenant's currency list on render.
            listCurrencies: () => of([{ code: 'NPR', name: 'Nepalese Rupee', symbol: 'Rs', isActive: true }]),
          },
        },
        { provide: PrintingService, useValue: {} },
        {
          provide: InboxService,
          useValue: {
            getPrefill: () => of(null),
            // Phase 27a's source-document panel lists linked inbox documents the moment it renders.
            listDocuments: () => of({ items: [], totalCount: 0, page: 1, pageSize: 10 }),
            contentUrl: () => '',
            linkDocument: () => of(undefined),
          },
        },
        {
          provide: ConfigurationService,
          useValue: {
            listTdsTypes: () => of([]),
            listCostTerms: () => of(costTerms),
            listCustomFieldDefinitions: () => of([]),
            getCustomFieldValues: () => of([]),
            setCustomFieldValues: () => of(undefined),
            listReportingTagCategories: () => of([]),
            listReportingTagOptions: () => of([]),
            getTransactionReportingTags: () => of([]),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => organizationId }, queryParamMap: { get: () => null } },
            paramMap: of({ get: (key: string) => (key === 'id' ? organizationId : routeBillId) }),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(PurchaseBillDetailPage);
    fixture.detectChanges();

    return { fixture, text: () => (fixture.nativeElement as HTMLElement).textContent ?? '' };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders an approved bill as a product-by-cost-term matrix', () => {
    const { text } = page();

    expect(text()).toContain('Additional Cost');
    // One column per cost term that allocated something, one row per goods line.
    expect(text()).toContain('Freight');
    expect(text()).toContain('Motorbike');
    expect(text()).toContain('Helmet');
    expect(text()).toContain('600.00');
    expect(text()).toContain('60.00');
  });

  it('shows what was capitalised into stock', () => {
    expect(page().text()).toContain('Capitalised into stock: 660.00');
  });

  it('says nothing about rounding when the allocation divided evenly', () => {
    expect(page().text()).not.toContain('rounding adjustment');
  });

  it('names the rounding residue rather than absorbing it', () => {
    const { text } = page(
      detail({ capitalisedAdditionalCost: 659.9999, additionalCostRoundingAdjustment: 0.0001 }),
    );

    expect(text()).toContain('rounding adjustment');
    // Four decimals, because the residue is legitimately smaller than a paisa (phase-25's lesson).
    expect(text()).toContain('0.0001');
  });

  it('leaves the Grand Total alone — additional cost is capitalised, not owed to the supplier', () => {
    const { fixture } = page();
    const component = fixture.componentInstance as unknown as { grandTotal: () => number };

    expect(component.grandTotal()).toBe(6600);
  });

  it('offers only the bill’s goods products, never a service line', () => {
    const { fixture } = page(
      detail({
        status: 'Draft',
        approvedAt: null,
        lines: [
          ...detail().lines,
          {
            id: 'l-consult',
            productId: 'p-consult',
            quantity: 1,
            rate: 900,
            vatRate: 'NoVat',
            expenditureClassification: 'Others',
            discountPct: 0,
            amount: 900,
            vatAmount: 0,
          },
        ],
      }),
    );
    const component = fixture.componentInstance as unknown as {
      goodsLineProducts: () => { id: string }[];
      additionalCostTerms: () => { id: string }[];
    };

    expect(component.goodsLineProducts().map((p) => p.id)).toEqual(['p-bike', 'p-helmet']);

    // And only the active AdditionalCost terms -- the ProductionCost half is Phase 25's, and the
    // live picker lists active terms only.
    expect(component.additionalCostTerms().map((t) => t.id)).toEqual(['ct-freight', 'ct-duty']);
  });

  it('renders the editable section on a draft, with the live defaults on a new row', () => {
    const { fixture, text } = page(detail({ status: 'Draft', approvedAt: null, additionalCosts: [] }));
    const component = fixture.componentInstance as unknown as {
      addAdditionalCost: () => void;
      additionalCosts: () => { costTermId: string; productId: string; method: string }[];
      additionalCostTotal: () => number;
    };

    expect(text()).toContain('+ Add Additional Cost');

    component.addAdditionalCost();
    fixture.detectChanges();

    const row = component.additionalCosts()[0];
    expect(row.productId).toBe('');
    expect(row.method).toBe('Value');
    expect(text()).toContain('Add product-wise');
    expect(text()).toContain('All Product');
    expect(component.additionalCostTotal()).toBe(0);
  });
});
