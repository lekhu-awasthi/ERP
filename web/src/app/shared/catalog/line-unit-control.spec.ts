import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import { Product, UnitOfMeasurement } from '../../core/catalog/catalog.models';
import { LineUnitControl, LineUnitOption } from './line-unit-control';

const PIECE = 'aaaaaaaa-0000-0000-0000-000000000001';
const CARTON = 'aaaaaaaa-0000-0000-0000-000000000002';
const GONE = 'aaaaaaaa-0000-0000-0000-000000000003';

const units: UnitOfMeasurement[] = [
  { id: PIECE, organizationId: 'o', name: 'Piece', shortName: 'PIS', isActive: true, createdAt: '' },
  { id: CARTON, organizationId: 'o', name: 'Carton', shortName: 'CTN', isActive: true, createdAt: '' },
];

function product(overrides: Partial<Product> = {}): Product {
  return {
    id: 'p1',
    organizationId: 'o',
    type: 'Goods',
    name: 'Steel rods',
    code: 'P0590',
    categoryId: 'c',
    primaryUnitId: PIECE,
    hsCode: null,
    availableForSale: true,
    sellingPrice: 1200,
    purchasePrice: 900,
    vatRate: 'NoVat',
    valuationMethod: 'Fifo',
    reOrderLevel: 0,
    trackInventory: true,
    isActive: true,
    createdAt: '',
    batchTracking: false,
    serialTracking: false,
    sku: null,
    barcode: null,
    parentProductId: null,
    hasVariants: false,
    secondaryUnits: [],
    locations: [],
    salesAccountId: null,
    ...overrides,
  } as Product;
}

const withCarton = product({
  secondaryUnits: [
    { id: 's1', productId: 'p1', unitId: CARTON, conversionRate: 12, sellingPrice: 1, purchasePrice: 1 },
  ],
});

@Component({
  imports: [LineUnitControl],
  template: `
    <app-line-unit
      [products]="products()"
      [units]="units()"
      [productId]="productId()"
      [unitId]="unitId()"
      (unitChange)="picked = $event"
    />
  `,
})
class Host {
  readonly products = signal<Product[]>([]);
  readonly units = signal<UnitOfMeasurement[]>(units);
  readonly productId = signal('p1');
  readonly unitId = signal<string | null>(null);
  picked: LineUnitOption | null = null;
}

function render(products: Product[], unitId: string | null = null) {
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.products.set(products);
  fixture.componentInstance.unitId.set(unitId);
  fixture.detectChanges();
  return fixture;
}

describe('LineUnitControl', () => {
  it('renders a plain label, not a dropdown, for a product with only its primary unit', () => {
    // The live product renders a greyed `cursor: not-allowed` label here rather than a one-option
    // select -- a control that is disabled, not absent (phase 44's parent-and-modifier shape).
    const fixture = render([product()]);
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('select')).toBeNull();
    expect(el.textContent?.trim()).toBe('PIS');
  });

  it('offers the whole matrix, primary included, when the product has a secondary unit', () => {
    // The vendor's own dropdown on Steel rods offered both BTL and PIS.
    const fixture = render([withCarton]);
    const options = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('option'),
    ).map((o) => o.textContent?.trim());

    expect(options).toEqual(['PIS', 'CTN']);
  });

  it('selects the primary when the line names no unit', () => {
    const select = render([withCarton]).nativeElement.querySelector('select') as HTMLSelectElement;

    expect(select.value).toBe(PIECE);
  });

  it('selects the line’s own unit when it names one', () => {
    const select = render([withCarton], CARTON).nativeElement.querySelector('select') as HTMLSelectElement;

    expect(select.value).toBe(CARTON);
  });

  it('emits the chosen unit with its rate and prices, so the caller can prefill Rate', () => {
    const fixture = render([withCarton]);
    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;

    select.value = CARTON;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(fixture.componentInstance.picked).toEqual({
      unitId: CARTON,
      shortName: 'CTN',
      conversionRate: 12,
      sellingPrice: 1,
      purchasePrice: 1,
    });
  });

  it('drops a secondary unit whose lookup row is missing rather than showing a blank option', () => {
    // A nameless option is one the user cannot tell apart from the next one -- worse than absent,
    // which is phase 40's rule about a control that owns its own label, applied to an option.
    const orphaned = product({
      secondaryUnits: [
        { id: 's1', productId: 'p1', unitId: GONE, conversionRate: 6, sellingPrice: 0, purchasePrice: 0 },
      ],
    });

    const el: HTMLElement = render([orphaned]).nativeElement;

    expect(el.querySelector('select')).toBeNull();
    expect(el.textContent?.trim()).toBe('PIS');
  });

  it('renders nothing rather than guessing while the line has no product yet', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.products.set([withCarton]);
    fixture.componentInstance.productId.set('');
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('select')).toBeNull();
    expect(el.textContent?.trim()).toBe('');
  });

  it('degrades to a blank label when the units lookup has not arrived', () => {
    // An empty store is the failure mode of a tenant whose units cannot be read. The form still
    // works and every line is in the primary unit, which is the pre-phase-52 behaviour.
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.products.set([withCarton]);
    fixture.componentInstance.units.set([]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('select')).toBeNull();
  });

  it('names itself to a screen reader', () => {
    const select = render([withCarton]).nativeElement.querySelector('select') as HTMLSelectElement;

    expect(select.getAttribute('aria-label')).toBe('Unit');
  });
});
