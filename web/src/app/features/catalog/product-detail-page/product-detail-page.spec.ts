import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import {
  Product,
  ProductCategory,
  ProductVariantPanel,
  UnitOfMeasurement,
} from '../../../core/catalog/catalog.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { ProductDetailPage } from './product-detail-page';

/**
 * Phase 45 -- the Secondary Units panel, which this phase turned from add-only into the reference
 * product's own three-verb table (ADD NEW plus a per-row Action column), and gated on the product's
 * variant role.
 *
 * <p>A rendering test rather than a browser pass, for phase-24's reason: the two `computed()` gates
 * added here decide whether a whole card appears, and "a control that hides itself owns its own
 * label" (phase-40) has a mirror -- a control that *fails* to hide is a 409 the user can only find
 * by pressing it. Both directions are asserted.</p>
 *
 * <p>It also closes phase 44's carried item #6: that phase added four Angular screens and no Angular
 * tests, and the rule is to close the habit at the first screen touched rather than widen it.</p>
 */
describe('ProductDetailPage — secondary units', () => {
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
  const boxUnit: UnitOfMeasurement = { ...pieceUnit, id: 'unit-box', name: 'Box', shortName: 'box' };
  const cartonUnit: UnitOfMeasurement = { ...pieceUnit, id: 'unit-carton', name: 'Carton', shortName: 'ctn' };

  const category: ProductCategory = {
    id: 'cat-1',
    organizationId,
    name: 'Apparel',
    parentCategoryId: null,
    isActive: true,
    createdAt: '2026-09-15T00:00:00Z',
  };

  function product(overrides: Partial<Product> = {}): Product {
    return {
      id: productId,
      organizationId,
      type: 'Goods',
      name: 'T-Shirt Blue',
      code: 'PRD-0002',
      categoryId: category.id,
      primaryUnitId: pieceUnit.id,
      hsCode: null,
      availableForSale: true,
      sellingPrice: 1000,
      purchasePrice: 800,
      vatRate: 'NoVat',
      valuationMethod: 'Fifo',
      reOrderLevel: 0,
      trackInventory: true,
      // Phase 51 -- both default off, which is what makes the feature additive.
      batchTracking: false,
      serialTracking: false,
      isActive: true,
      createdAt: '2026-09-15T00:00:00Z',
      sku: null,
      barcode: null,
      parentProductId: null,
      hasVariants: false,
      secondaryUnits: [
        {
          id: 'su-1',
          productId,
          unitId: boxUnit.id,
          conversionRate: 12,
          sellingPrice: 12000,
          purchasePrice: 9600,
        },
      ],
      locations: [],
      salesAccountId: null,
      salesReturnAccountId: null,
      purchaseAccountId: null,
      purchaseReturnAccountId: null,
      ...overrides,
    };
  }

  let fixture: ComponentFixture<ProductDetailPage>;
  let deleted: string[];
  let updated: { id: string; conversionRate: number }[];

  async function render(data: Product): Promise<void> {
    deleted = [];
    updated = [];

    const catalog: Partial<CatalogService> = {
      getProduct: (): Observable<Product> => of(data),
      listProductCategories: (): Observable<ProductCategory[]> => of([category]),
      listUnitsOfMeasurement: (): Observable<UnitOfMeasurement[]> => of([pieceUnit, boxUnit, cartonUnit]),
      // The variant panel is a child component of this page; it loads on init whenever it renders.
      getProductVariants: (): Observable<ProductVariantPanel> =>
        of({ productId: data.id, hasVariants: data.hasVariants, attributesUsed: [], variants: [] }),
      deleteSecondaryUnit: (_org: string, _pid: string, secondaryUnitId: string) => {
        deleted.push(secondaryUnitId);
        return of(undefined as void);
      },
      updateSecondaryUnit: (_org: string, _pid: string, secondaryUnitId: string, request) => {
        updated.push({ id: secondaryUnitId, conversionRate: request.conversionRate });
        return of({
          id: secondaryUnitId,
          productId,
          unitId: boxUnit.id,
          conversionRate: request.conversionRate,
          sellingPrice: request.sellingPrice,
          purchasePrice: request.purchasePrice,
        });
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
            snapshot: { paramMap: new Map([['id', organizationId]]) },
            paramMap: of(new Map([['id', organizationId], ['productId', productId]])),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProductDetailPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  function buttonLabelled(label: string): HTMLButtonElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector(`button[aria-label="${label}"]`);
  }

  it('renders every field a secondary-unit row carries, and an Action column', async () => {
    await render(product());

    expect(text()).toContain('Secondary Units');
    expect(text()).toContain('Box');
    expect(text()).toContain('12');
    expect(text()).toContain('Action');

    expect(buttonLabelled('Edit the Box secondary unit')).not.toBeNull();
    expect(buttonLabelled('Delete the Box secondary unit')).not.toBeNull();
  });

  it('hides the whole panel on a variant parent, which holds no stock to convert', async () => {
    await render(product({ hasVariants: true, secondaryUnits: [] }));

    expect(text()).not.toContain('Secondary Units');
  });

  /**
   * The exception to the rule above, and the reason the delete is the one verb a parent keeps: a
   * product can hold secondary units and be promoted to a parent afterwards. Hiding the card then
   * would leave those rows invisible AND unremovable. Found in the browser pass.
   */
  it('keeps the panel on a parent that still holds stale rows, with Delete but not Edit or Add', async () => {
    await render(product({ hasVariants: true }));

    expect(text()).toContain('Secondary Units');
    expect(text()).toContain('these units');
    expect(buttonLabelled('Delete the Box secondary unit')).not.toBeNull();
    expect(buttonLabelled('Edit the Box secondary unit')).toBeNull();

    const add = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).filter((b) => b.textContent?.trim().endsWith('Add'));
    expect(add.length).toBe(0);
  });

  it('shows the panel on a variant child, and says the units are its own', async () => {
    await render(product({ parentProductId: 'parent-1' }));

    expect(text()).toContain('Secondary Units');
    expect(text()).toContain("This variant's own units");
  });

  // TestBed can only be configured once per test, so the two halves of "exactly complementary" are
  // two tests rather than one with two renders.
  it('offers the variant panel on a parent', async () => {
    await render(product({ hasVariants: true, secondaryUnits: [] }));

    expect(text()).toContain('Variants');
  });

  it('withholds the variant panel from a variant child', async () => {
    await render(product({ parentProductId: 'parent-1' }));

    // A variant cannot itself have variants -- the server 409s, so the editor is not offered.
    expect(text()).not.toContain('Set Up Variants');
  });

  it('asks before deleting a row, and sends the delete only on confirm', async () => {
    await render(product());

    buttonLabelled('Delete the Box secondary unit')!.click();
    fixture.detectChanges();

    expect(text()).toContain('Remove Box?');
    expect(deleted).toEqual([]);

    const confirm = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button.btn-danger'),
    ).find((b) => b.textContent?.trim() === 'Remove');
    confirm!.click();
    fixture.detectChanges();

    expect(deleted).toEqual(['su-1']);
  });

  it('disables the unit select while editing, because the unit is the row identity', async () => {
    await render(product());

    buttonLabelled('Edit the Box secondary unit')!.click();
    fixture.detectChanges();

    const select = (fixture.nativeElement as HTMLElement).querySelector<HTMLSelectElement>(
      '#product-detail-page-unit',
    );
    expect(select).not.toBeNull();
    expect(select!.disabled).toBe(true);
  });

  it('does not offer a unit the product already uses when adding', async () => {
    await render(product());

    const add = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((b) => b.textContent?.trim().endsWith('Add'));
    add!.click();
    fixture.detectChanges();

    const options = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLOptionElement>(
        '#product-detail-page-unit option',
      ),
    ).map((o) => o.textContent?.trim());

    // Box already has a row and Piece is the primary unit; both are refused with a 409 server-side.
    expect(options).toContain('Carton');
    expect(options).not.toContain('Box');
    expect(options).not.toContain('Piece');
  });
});
