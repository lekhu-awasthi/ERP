import { slabDiscountPct, withSchemeRate } from './discount-schemes';

/** Phase 97 -- the browser's half of the server's `DiscountSchemeRates`: the same ladder rule, and a line moved only while
 * it follows its scheme. */
describe('discount schemes', () => {
  const slabs = [
    { minimumAmount: 1000, discountPct: 2 },
    { minimumAmount: 5000, discountPct: 5 },
  ];

  it('gives a bill the highest step it reaches, and nothing below the first', () => {
    expect(slabDiscountPct(slabs, 999.99)).toBe(0);
    expect(slabDiscountPct(slabs, 1000)).toBe(2);
    expect(slabDiscountPct(slabs, 4999.99)).toBe(2);
    expect(slabDiscountPct(slabs, 5000)).toBe(5);
    expect(slabDiscountPct([], 1_000_000)).toBe(0);
  });

  it('moves a following line to its product\'s percent, or to none', () => {
    const discounts = { products: [{ productId: 'p1', discountPct: 7.5 }], slabs: [] };

    expect(withSchemeRate({ productId: 'p1', discountPct: 0, schemeFollows: true }, discounts).discountPct).toBe(7.5);
    expect(withSchemeRate({ productId: 'p2', discountPct: 7.5, schemeFollows: true }, discounts).discountPct).toBe(0);
  });

  it('never moves a line the user priced or one read back from a saved draft', () => {
    const discounts = { products: [{ productId: 'p1', discountPct: 7.5 }], slabs: [] };

    expect(withSchemeRate({ productId: 'p1', discountPct: 3, schemeFollows: false }, discounts).discountPct).toBe(3);
    expect(withSchemeRate({ productId: 'p1', discountPct: 0 }, discounts).discountPct).toBe(0);
  });
});
