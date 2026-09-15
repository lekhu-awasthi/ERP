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
      locationId: null,
      code: 'PB0001',
      date: '2026-01-10',
      dueDate: '2026-01-10',
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
            // Phase 35a's shared billing-location picker reads both of these on render.
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
            listCreditTerms: () => of([]),
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

  /**
   * Phase 45 -- the decision phase 38 left open, recorded as a test rather than as a sentence.
   *
   * <p>Phase 38 shipped the Import drawer replacing a product's rows rather than merging into them,
   * and named the cost to the user: "a user who wants to add a second cost term to a product already
   * in the grid by file must include the existing amount too". Phase 45 keeps replace, and this
   * pins both halves of what that means, because only one of them is obvious.</p>
   *
   * <p><b>Why keep it.</b> The file is generated from the bill in front of you -- one row per goods
   * line, one column per tenant cost term -- so it is not a fragment naming a product, it is the
   * whole matrix for the products it names. A merge would make re-uploading a corrected spreadsheet
   * silently additive: the user who fixes Freight from 600 to 60 and uploads again would get 660,
   * a number nobody notices until it has reached the FIFO layers. That asymmetry -- a correction
   * reading as an addition -- is worse than the stated cost of restating an amount, and replace is
   * what every other child-collection editor in this codebase does (phase-4 bug #1's
   * snapshot-and-RemoveRange idiom).</p>
   *
   * <p><b>The half that is not obvious</b> is that "replace" is scoped per product, not per grid: a
   * row whose product the file never mentions survives, because the file makes no claim about
   * it.</p>
   */
  it('replaces a products rows from an uploaded grid and leaves unmentioned products alone', () => {
    const { fixture } = page(detail({ status: 'Draft', approvedAt: null, additionalCosts: [] }));
    const component = fixture.componentInstance as unknown as {
      applyAdditionalCostCells: (
        cells: readonly { productId: string; costTermId: string; amount: number }[],
      ) => void;
      additionalCosts: {
        (): { costTermId: string; productId: string; amount: number }[];
        set: (rows: { key: number; costTermId: string; productId: string; method: string; amount: number }[]) => void;
      };
    };

    component.additionalCosts.set([
      { key: 1, costTermId: 'ct-freight', productId: 'p-bike', method: 'Value', amount: 600 },
      { key: 2, costTermId: 'ct-duty', productId: 'p-bike', method: 'Value', amount: 100 },
      { key: 3, costTermId: 'ct-freight', productId: 'p-helmet', method: 'Value', amount: 60 },
    ]);

    // The file names only the motorbike, and gives it one term.
    component.applyAdditionalCostCells([{ productId: 'p-bike', costTermId: 'ct-freight', amount: 900 }]);

    const rows = component.additionalCosts();

    // Replace, not merge: the bike's Custom Duty row is gone rather than kept beside the new Freight.
    const bike = rows
      .filter((r) => r.productId === 'p-bike')
      .map((r) => ({ costTermId: r.costTermId, amount: r.amount }));
    expect(bike).toEqual([{ costTermId: 'ct-freight', amount: 900 }]);

    // ...but per product: the helmet was not in the file, so the file said nothing about it.
    const helmet = rows.filter((r) => r.productId === 'p-helmet');
    expect(helmet.map((r) => r.amount)).toEqual([60]);
  });

  it('re-uploading a corrected grid overwrites rather than accumulating', () => {
    const { fixture } = page(detail({ status: 'Draft', approvedAt: null, additionalCosts: [] }));
    const component = fixture.componentInstance as unknown as {
      applyAdditionalCostCells: (
        cells: readonly { productId: string; costTermId: string; amount: number }[],
      ) => void;
      additionalCostTotal: () => number;
    };

    component.applyAdditionalCostCells([{ productId: 'p-bike', costTermId: 'ct-freight', amount: 600 }]);
    component.applyAdditionalCostCells([{ productId: 'p-bike', costTermId: 'ct-freight', amount: 60 }]);

    // 60, not 660 -- the whole reason replace was kept.
    expect(component.additionalCostTotal()).toBe(60);
  });
});
