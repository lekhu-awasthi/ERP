import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import {
  Product,
  ProductBatchTabRow,
  ProductCategory,
  ProductSerialTabRow,
  ProductVariantPanel,
  UnitOfMeasurement,
} from '../../../core/catalog/catalog.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { ProductDetailPage } from './product-detail-page';

/**
 * Phase 51 -- the two traceability toggles and the two tabs they gate.
 *
 * <p>Rendering tests rather than a browser pass, for phase 45's reason repeated: these gates decide
 * whether a whole card appears, and a control that fails to hide is a 409 the user can only find by
 * pressing it. Both directions are asserted, because the *additive* claim -- a tenant that never
 * turns either flag on sees no new control -- is the whole justification for shipping this in one
 * phase, and a claim nobody checks is a claim.</p>
 */
describe('ProductDetailPage — batch and serial tracking', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const productId = '22222222-2222-2222-2222-222222222222';

  const pieceUnit: UnitOfMeasurement = {
    id: 'unit-piece',
    organizationId,
    name: 'Piece',
    shortName: 'pc',
    isActive: true,
    createdAt: '2026-09-15T00:00:00Z',
  };

  const category: ProductCategory = {
    id: 'cat-1',
    organizationId,
    name: 'Pharma',
    parentCategoryId: null,
    isActive: true,
    createdAt: '2026-09-15T00:00:00Z',
  };

  const batchRow: ProductBatchTabRow = {
    id: 'b1',
    batchNo: 'BATCH123',
    manufactureDate: '2026-09-01',
    expiryDate: '2026-09-03',
    quantity: 2,
    unitName: 'CTN',
    warehouseId: null,
    warehouseName: null,
  };

  const serialRow: ProductSerialTabRow = {
    serialNo: 'A1',
    warehouseId: 'w1',
    warehouseName: 'Kathmandu',
    createdAt: '2026-09-02T04:15:00+00:00',
  };

  function product(overrides: Partial<Product> = {}): Product {
    return {
      id: productId,
      organizationId,
      type: 'Goods',
      name: 'Paracetamol',
      code: 'PRD-0001',
      categoryId: category.id,
      primaryUnitId: pieceUnit.id,
      hsCode: null,
      availableForSale: true,
      sellingPrice: 100,
      purchasePrice: 80,
      vatRate: 'NoVat',
      valuationMethod: 'Fifo',
      reOrderLevel: 0,
      trackInventory: true,
      batchTracking: false,
      serialTracking: false,
      isActive: true,
      createdAt: '2026-09-15T00:00:00Z',
      sku: null,
      barcode: null,
      parentProductId: null,
      hasVariants: false,
      secondaryUnits: [],
      locations: [],
      salesAccountId: null,
      salesReturnAccountId: null,
      purchaseAccountId: null,
      purchaseReturnAccountId: null,
      ...overrides,
    };
  }

  let fixture: ComponentFixture<ProductDetailPage>;
  let batchesRequested: number;
  let serialsRequested: number;

  async function render(data: Product): Promise<HTMLElement> {
    batchesRequested = 0;
    serialsRequested = 0;

    const catalog: Partial<CatalogService> = {
      getProduct: (): Observable<Product> => of(data),
      listProductCategories: (): Observable<ProductCategory[]> => of([category]),
      listUnitsOfMeasurement: (): Observable<UnitOfMeasurement[]> => of([pieceUnit]),
      getProductVariants: (): Observable<ProductVariantPanel> =>
        of({ productId: data.id, hasVariants: data.hasVariants, attributesUsed: [], variants: [] }),
      listProductBatches: (): Observable<ProductBatchTabRow[]> => {
        batchesRequested += 1;
        return of([batchRow]);
      },
      listProductSerials: (): Observable<ProductSerialTabRow[]> => {
        serialsRequested += 1;
        return of([serialRow]);
      },
    };

    const accounting: Partial<AccountingService> = {
      listAllAccounts: (): Observable<never[]> => of([]),
    };

    await TestBed.configureTestingModule({
      imports: [ProductDetailPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CatalogService, useValue: catalog },
        { provide: AccountingService, useValue: accounting },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: (key: string) => (key === 'id' ? organizationId : productId) } },
            paramMap: of({ get: (key: string) => (key === 'id' ? organizationId : productId) }),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProductDetailPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('shows neither tab, and asks for neither, on an untracked product', async () => {
    // The additive claim, asserted rather than believed: a tenant that never turns a flag on pays
    // nothing -- not even a request.
    const element = await render(product());

    expect(element.textContent).not.toContain('Serial Number');
    expect(batchesRequested).toBe(0);
    expect(serialsRequested).toBe(0);
  });

  it('shows the Batch tab, with its quantity, for a batch-tracked product', async () => {
    const element = await render(product({ batchTracking: true }));

    expect(batchesRequested).toBe(1);
    expect(element.textContent).toContain('BATCH123');
    expect(element.textContent).toContain('CTN');
  });

  it('asks for serials only when the serial flag is on', async () => {
    const element = await render(product({ batchTracking: true }));

    expect(serialsRequested).toBe(0);
    expect(element.textContent).not.toContain('A1');
  });

  it('shows the Serial Number tab for a serial-tracked product', async () => {
    const element = await render(product({ serialTracking: true }));

    expect(serialsRequested).toBe(1);
    expect(element.textContent).toContain('A1');
    expect(element.textContent).toContain('Kathmandu');
  });

  it('shows both tabs when both flags are on', async () => {
    // The two toggles are independent live, so a serialised item belonging to a batch is a shape
    // the model has to admit -- a layer of quantity one that also carries a BatchId.
    const element = await render(product({ batchTracking: true, serialTracking: true }));

    expect(element.textContent).toContain('BATCH123');
    expect(element.textContent).toContain('A1');
  });
});
