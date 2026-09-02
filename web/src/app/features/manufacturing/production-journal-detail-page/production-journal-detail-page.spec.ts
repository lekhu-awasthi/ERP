import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { ProductionJournalDetail } from '../../../core/manufacturing/manufacturing.models';
import { ManufacturingService } from '../../../core/manufacturing/manufacturing.service';
import { ProductionJournalDetailPage } from './production-journal-detail-page';

/**
 * <b>Phase 23's bug #1, applied to this phase's richest target.</b> That bug was a report page whose
 * DTO carried four real fields the template had no element for: every automated check was green and
 * the feature was invisible. A six-figure cost roll-up is exactly that shape, so these tests assert
 * the numbers reach the screen, not merely that the component compiles.
 */
describe('ProductionJournalDetailPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function detail(overrides: Partial<ProductionJournalDetail> = {}): ProductionJournalDetail {
    return {
      id: '22222222-2222-2222-2222-222222222222',
      code: 'PJ0001',
      date: '2026-01-25',
      reference: null,
      productId: 'p-finished',
      productName: 'Finished Widget',
      productCode: 'P0001',
      unitName: 'pc',
      outputQuantity: 10,
      warehouseId: 'w-1',
      billOfMaterialsId: null,
      notes: null,
      status: 'Approved',
      referrerType: null,
      referrerId: null,
      rawMaterialCost: 1200,
      productionExpenseCost: 300,
      totalCostOfProduction: 1500,
      costAllocatedToByProduct: 300,
      finishedGoodsCost: 1200,
      finishedGoodsUnitCost: 120,
      costRoundingAdjustment: 0,
      approvedAt: '2026-01-25T05:00:00Z',
      voidedAt: null,
      createdAt: '2026-01-25T04:00:00Z',
      rawMaterials: [
        {
          id: 'rm-1',
          productId: 'p-raw',
          productName: 'Steel Sheet',
          productCode: 'P0002',
          unitName: 'pc',
          quantity: 20,
          rate: 60,
          amount: 1200,
        },
      ],
      byProducts: [
        {
          id: 'bp-1',
          productId: 'p-scrap',
          productName: 'Steel Offcut',
          productCode: 'P0003',
          unitName: 'pc',
          costAllocationPct: 20,
          quantity: 6,
          rate: 50,
          amount: 300,
        },
      ],
      expenses: [{ id: 'ex-1', costTermId: 'ct-1', costTermName: 'Direct Labor Costs', amount: 300 }],
      glLines: [
        { id: 'gl-1', accountId: 'acc-inventory', debit: 1500, credit: 0 },
        { id: 'gl-2', accountId: 'acc-inventory', debit: 0, credit: 1200 },
        { id: 'gl-3', accountId: 'acc-production', debit: 0, credit: 300 },
      ],
      ...overrides,
    };
  }

  function page(journal: ProductionJournalDetail = detail()) {
    const manufacturingService = {
      getProductionJournal: (): Observable<ProductionJournalDetail> => of(journal),
    };

    TestBed.configureTestingModule({
      imports: [ProductionJournalDetailPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ManufacturingService, useValue: manufacturingService },
        { provide: CatalogService, useValue: { listAllProducts: () => of([]) } },
        { provide: OrganizationsService, useValue: { listWarehouses: () => of([]) } },
        { provide: AccountingService, useValue: { listAllAccounts: () => of([{ id: 'acc-inventory', code: '1200', name: 'Inventory' }]) } },
        { provide: ConfigurationService, useValue: { listCostTerms: () => of([]) } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => organizationId }, queryParamMap: { get: () => null } },
            paramMap: of({ get: (key: string) => (key === 'id' ? organizationId : journal.id) }),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(ProductionJournalDetailPage);
    fixture.detectChanges();

    return { fixture, text: () => (fixture.nativeElement as HTMLElement).textContent ?? '' };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders every figure of the cost roll-up, not just the ones it happens to use elsewhere', () => {
    const { text } = page();

    expect(text()).toContain('Cost Roll-up');
    expect(text()).toContain('Raw Material Cost');
    expect(text()).toContain('Production Expenses');
    expect(text()).toContain('Total Cost of Production');
    expect(text()).toContain('Cost Allocated to By-product');
    expect(text()).toContain('Finished Goods Cost');
    expect(text()).toContain('Cost Per Unit');

    // The values themselves, through the amount pipe -- which is also the proof the pipe is wired in.
    expect(text()).toContain('1,200.00');
    expect(text()).toContain('1,500.00');
    expect(text()).toContain('300.00');
    expect(text()).toContain('120.00');
  });

  it('hides the rounding adjustment when it is zero', () => {
    expect(page().text()).not.toContain('Rounding Adjustment');
  });

  it('names the rounding adjustment rather than hiding it when the cost did not divide evenly', () => {
    const { text } = page(
      detail({ finishedGoodsCost: 1200.008, finishedGoodsUnitCost: 120.0008, costRoundingAdjustment: -0.008 }),
    );

    expect(text()).toContain('Rounding Adjustment');
  });

  it('shows the approved run\'s real rates and amounts on the raw-material lines', () => {
    const { text } = page();

    expect(text()).toContain('Steel Sheet');
    expect(text()).toContain('60.00');
    expect(text()).toContain('1,200.00');
  });

  it('shows the posted GL transactions', () => {
    const { text } = page();

    expect(text()).toContain('GL Transactions');
    expect(text()).toContain('1200 — Inventory');
  });

  it('offers Void on an approved run and neither Save nor Approve', () => {
    const { text } = page();

    expect(text()).toContain('Void this Production Journal');
    expect(text()).not.toContain('Create Production Journal');
  });

  it('shows no roll-up at all for a draft, because a draft has not been costed', () => {
    const { text } = page(
      detail({
        status: 'Draft',
        code: 'DRAFT',
        rawMaterialCost: null,
        productionExpenseCost: null,
        totalCostOfProduction: null,
        costAllocatedToByProduct: null,
        finishedGoodsCost: null,
        finishedGoodsUnitCost: null,
        costRoundingAdjustment: null,
        glLines: null,
        rawMaterials: [
          {
            id: 'rm-1',
            productId: 'p-raw',
            productName: 'Steel Sheet',
            productCode: 'P0002',
            unitName: 'pc',
            quantity: 20,
            rate: null,
            amount: null,
          },
        ],
      }),
    );

    expect(text()).not.toContain('Cost Roll-up');
    expect(text()).not.toContain('GL Transactions');

    // And it says why the rates are missing rather than showing empty columns.
    expect(text()).toContain('Rates are resolved from the FIFO stock ledger');
  });
});
