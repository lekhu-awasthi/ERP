import { TestBed } from '@angular/core/testing';

import { Product } from '../../core/catalog/catalog.models';
import { SalesPriceNote, rateIsLocked } from './sales-price-powers';

describe('Phase 81 -- the ERP price powers on a sales form', () => {
  const product = {
    id: 'p1',
    primaryUnitId: 'pc',
    sellingPrice: 150,
    secondaryUnits: [{ unitId: 'box', sellingPrice: 0 }],
  } as unknown as Product;
  const clerk = { canDiscount: false, canChangeRate: false, atLocation: true };

  it('locks a priced base-currency rate for a user without the change-rate key', () => {
    expect(rateIsLocked(clerk, 'NPR', product, '')).toBe(true);
    expect(rateIsLocked(clerk, 'NPR', product, 'pc')).toBe(true);
  });

  it('leaves the rate open where the server has nothing to keep, or before the answer arrives', () => {
    expect(rateIsLocked({ canDiscount: false, canChangeRate: true, atLocation: true }, 'NPR', product, '')).toBe(false);
    expect(rateIsLocked(clerk, 'USD', product, '')).toBe(false);
    expect(rateIsLocked(clerk, 'NPR', product, 'box')).toBe(false);
    expect(rateIsLocked(clerk, 'NPR', undefined, '')).toBe(false);
    expect(rateIsLocked(null, 'NPR', product, '')).toBe(false);
  });

  it('names each key it withholds, and says nothing to someone who holds both', () => {
    const fixture = TestBed.createComponent(SalesPriceNote);
    fixture.componentRef.setInput('powers', clerk);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Sales.Invoice.Discount');
    expect(text).toContain('Sales.Invoice.ChangeRate');

    expect(text).toContain('at this location');
    expect(text).toContain('can still be removed');

    fixture.componentRef.setInput('powers', { canDiscount: true, canChangeRate: true, atLocation: true });
    fixture.detectChanges();
    expect(((fixture.nativeElement as HTMLElement).textContent ?? '').trim()).toBe('');
  });

  it('says "in this organization" when the document carries no location (phase 83)', () => {
    const fixture = TestBed.createComponent(SalesPriceNote);
    fixture.componentRef.setInput('powers', { ...clerk, atLocation: false });
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Sales.Invoice.Discount permission in this organization');
    expect(text).toContain('Sales.Invoice.ChangeRate permission in this organization');
    expect(text).not.toContain('at this location');
  });
});
