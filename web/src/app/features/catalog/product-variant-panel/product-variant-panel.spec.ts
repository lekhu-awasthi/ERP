import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of } from 'rxjs';

import { ProductVariantPanel } from '../../../core/catalog/catalog.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { ProductVariantPanelComponent } from './product-variant-panel';

/**
 * Phase 24's variant panel.
 *
 * <p>This is a rendering test rather than a browser pass for a specific reason: phase-23's bug #1
 * was a report whose DTO carried four fields end to end that the template had no column for --
 * every automated check green, the feature invisible. The variant panel carries six per-variant
 * fields (name, combination, code, SKU/barcode, both prices), so each is asserted to actually reach
 * the DOM rather than merely to be fetched.</p>
 */
describe('ProductVariantPanelComponent', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const productId = '22222222-2222-2222-2222-222222222222';

  function panel(overrides: Partial<ProductVariantPanel> = {}): ProductVariantPanel {
    return {
      productId,
      hasVariants: true,
      attributesUsed: [
        {
          attributeId: 'attr-color',
          attributeName: 'Colour',
          options: [
            { optionId: 'opt-blue', value: 'Blue' },
            { optionId: 'opt-red', value: 'Red' },
          ],
        },
        {
          attributeId: 'attr-size',
          attributeName: 'Size',
          options: [{ optionId: 'opt-large', value: 'Large' }],
        },
      ],
      variants: [
        {
          id: 'var-1',
          parentProductId: productId,
          code: 'Product-0002',
          name: 'T-Shirt Large Blue',
          sku: 'TS-L-BL',
          barcode: '5901234123457',
          sellingPrice: 1250.5,
          purchasePrice: 900,
          isActive: true,
          attributeValues: [
            { attributeId: 'attr-color', attributeName: 'Colour', optionId: 'opt-blue', optionValue: 'Blue' },
            { attributeId: 'attr-size', attributeName: 'Size', optionId: 'opt-large', optionValue: 'Large' },
          ],
        },
      ],
      ...overrides,
    };
  }

  let fixture: ComponentFixture<ProductVariantPanelComponent>;

  async function render(data: ProductVariantPanel): Promise<void> {
    const stub: Partial<CatalogService> = {
      getProductVariants: (): Observable<ProductVariantPanel> => of(data),
    };

    await TestBed.configureTestingModule({
      imports: [ProductVariantPanelComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: CatalogService, useValue: stub }],
    }).compileComponents();

    fixture = TestBed.createComponent(ProductVariantPanelComponent);
    fixture.componentRef.setInput('organizationId', organizationId);
    fixture.componentRef.setInput('productId', productId);
    fixture.componentRef.setInput('productName', 'T-Shirt');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  it('renders every field the variant DTO carries, not just the name', async () => {
    await render(panel());

    expect(text()).toContain('T-Shirt Large Blue');
    expect(text()).toContain('Product-0002');
    expect(text()).toContain('TS-L-BL');
    expect(text()).toContain('5901234123457');

    // Prices go through the shared `amount` pipe (phase-23's sweep), so they are grouped.
    expect(text()).toContain('1,250.50');
    expect(text()).toContain('900.00');

    // The combination is shown as its own column, not left implicit in the composed name.
    expect(text()).toContain('Colour: Blue');
    expect(text()).toContain('Size: Large');
  });

  it('shows the attributes the product offers, including options with no variant yet', async () => {
    await render(panel());

    // Red is offered but unused -- the pool is not the set of variants.
    expect(text()).toContain('Red');
    expect(text()).toContain('Colour');
    expect(text()).toContain('Size');
  });

  it('says how many variants Generate All would produce, before it is pressed', async () => {
    // 2 colours x 1 size = 2. A user should not have to guess what a matrix button will do.
    await render(panel());

    expect(text()).toContain('Generate All (2)');
  });

  it('offers no variant table until the product has attributes', async () => {
    await render(panel({ hasVariants: false, attributesUsed: [], variants: [] }));

    expect(text()).toContain('This product has no variants');
    expect(text()).not.toContain('Generate All');
  });

  it('prompts to generate or add when attributes exist but no variant does yet', async () => {
    await render(panel({ hasVariants: false, variants: [] }));

    expect(text()).toContain('No variants yet');
    expect(text()).toContain('Generate All (2)');
  });

  it('marks an inactive variant rather than hiding it', async () => {
    const data = panel();
    data.variants[0].isActive = false;
    await render(data);

    expect(text()).toContain('T-Shirt Large Blue');
    expect(text()).toContain('Inactive');
  });

  it('renders a dash for a variant with no SKU rather than an empty cell', async () => {
    const data = panel();
    data.variants[0].sku = null;
    data.variants[0].barcode = null;
    await render(data);

    expect(text()).toContain('—');
  });
});
