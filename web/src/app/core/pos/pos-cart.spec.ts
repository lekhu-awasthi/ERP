import { PosCart, cartStorageKey } from './pos-cart';
import { PosProduct, PosTill } from './pos.models';

/** A Storage double: a Map behind the interface, so a test can look at exactly what was written. */
class MemoryStorage implements Storage {
  private readonly map = new Map<string, string>();

  get length(): number {
    return this.map.size;
  }

  clear(): void {
    this.map.clear();
  }

  getItem(key: string): string | null {
    return this.map.get(key) ?? null;
  }

  key(index: number): string | null {
    return [...this.map.keys()][index] ?? null;
  }

  removeItem(key: string): void {
    this.map.delete(key);
  }

  setItem(key: string, value: string): void {
    this.map.set(key, value);
  }
}

/** Storage that refuses everything, as a private window or a full quota can. */
class BrokenStorage extends MemoryStorage {
  override getItem(): string | null {
    throw new Error('blocked');
  }

  override setItem(): void {
    throw new Error('quota');
  }
}

const ORG = 'org-1';

function till(locationId = 'loc-1', overrides: Partial<PosTill> = {}): PosTill {
  return {
    locationId,
    locationCode: 'HO',
    locationName: 'Thamel',
    posMode: 'Retail',
    availableTabs: ['Retail', 'Delivery'],
    defaultTab: 'Retail',
    serviceChargeEnabled: true,
    serviceChargeRate: 10,
    roundOffEnabled: true,
    cashVerificationRequired: false,
    denominations: [1000, 500, 100],
    printInvoice: true,
    abbreviatedTaxInvoiceEnabled: false,
    isVatRegistered: true,
    warehouseId: 'wh-1',
    warehouseName: 'Main',
    walkInCustomer: { id: 'walk-in', code: 'WALKIN', name: 'Cash Customer' },
    paymentModes: [],
    categories: [],
    canSellOnCredit: true,
    ...overrides,
  };
}

const momo: PosProduct = {
  id: 'momo', code: 'P0001', name: 'Chicken Momo', categoryId: 'food', type: 'Service', vatRate: 'ThirteenPercentVat',
  rate: 200, serviceChargeApplicable: true, batchTracking: false, serialTracking: false, barcode: null,
  units: [{ unitId: 'plate', shortName: 'plt', conversionRate: 1, rate: 200 }],
};

const coke: PosProduct = {
  id: 'coke', code: 'P0002', name: 'Coke 250ml', categoryId: 'drinks', type: 'Goods', vatRate: 'ThirteenPercentVat',
  rate: 60, serviceChargeApplicable: false, batchTracking: false, serialTracking: false, barcode: '8901',
  units: [
    { unitId: 'pc', shortName: 'pc', conversionRate: 1, rate: 60 },
    { unitId: 'crate', shortName: 'crt', conversionRate: 24, rate: 1200 },
  ],
};

describe('PosCart', () => {
  it("bills phase 59's table: service charge only on the flagged product, rounded to the rupee", () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new MemoryStorage());

    cart.add(momo);
    cart.add(momo);
    cart.add(coke);
    cart.add(coke);

    expect(cart.lines().length).toBe(2);
    expect(cart.bill().serviceCharge).toBe(40);
    expect(cart.bill().vat).toBe(72.8);
    expect(cart.bill().roundOff).toBe(0.2);
    expect(cart.bill().grandTotal).toBe(633);
  });

  it('charges no service charge where the location does not, whatever the product says', () => {
    const cart = new PosCart(till('loc-1', { serviceChargeEnabled: false, serviceChargeRate: 0 }), ORG, 'ses-1', new MemoryStorage());

    cart.add(momo);

    expect(cart.bill().serviceCharge).toBe(0);
    expect(cart.bill().grandTotal).toBe(226);
  });

  it('re-prices a line at the catalogue rate of the unit it is changed to, and sends that unit', () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new MemoryStorage());
    cart.add(coke);
    const key = cart.lines()[0].key;

    cart.setUnit(key, 'crate');

    expect(cart.lines()[0].rate).toBe(1200);
    expect(cart.toSaleLines()[0]).toEqual({
      productId: 'coke', quantity: 1, rate: 1200, vatRate: null, discountPct: 0, unitId: 'crate',
    });

    // The primary unit travels as null, as every ERP form sends it.
    cart.setUnit(key, 'pc');
    expect(cart.toSaleLines()[0].unitId).toBeNull();
  });

  it('refuses a quantity, rate or discount the server would refuse, leaving the line as it was', () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new MemoryStorage());
    cart.add(coke);
    const key = cart.lines()[0].key;

    cart.setQuantity(key, 0);
    cart.setRate(key, -1);
    cart.setLineDiscount(key, 101);

    expect(cart.lines()[0]).toEqual(expect.objectContaining({ quantity: 1, rate: 60, discountPct: 0 }));
  });

  it('survives a reload at the same till in the same session', () => {
    const storage = new MemoryStorage();
    const first = new PosCart(till(), ORG, 'ses-1', storage);
    first.add(momo);
    first.setCustomer({ id: 'c1', code: 'C0001', name: 'Acme Retail' });

    const reloaded = new PosCart(till(), ORG, 'ses-1', storage);

    expect(reloaded.lines().map((x) => x.product.id)).toEqual(['momo']);
    expect(reloaded.customer()?.name).toBe('Acme Retail');
  });

  it('never shows one till’s cart at another (the vendor’s defect 8)', () => {
    const storage = new MemoryStorage();
    const thamel = new PosCart(till('loc-1'), ORG, 'ses-1', storage);
    thamel.add(momo);

    const lakeside = new PosCart(till('loc-2'), ORG, 'ses-2', storage);

    expect(lakeside.lines()).toEqual([]);
    // ...and opening Lakeside's cart did not disturb Thamel's.
    expect(storage.getItem(cartStorageKey(ORG, 'loc-1', 'ses-1'))).not.toBeNull();
  });

  it("sweeps what an earlier session at the same till left, so a closed drawer's holds do not come back", () => {
    const storage = new MemoryStorage();
    const yesterday = new PosCart(till('loc-1'), ORG, 'ses-1', storage);
    yesterday.add(coke);
    yesterday.hold();

    const today = new PosCart(till('loc-1'), ORG, 'ses-2', storage);

    expect(today.holds()).toEqual([]);
    expect(storage.getItem(cartStorageKey(ORG, 'loc-1', 'ses-1'))).toBeNull();
  });

  it('parks a cart, and parks the cart in progress rather than losing it on a recall', () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new MemoryStorage());
    cart.add(momo);
    cart.setCustomer({ id: 'c1', code: 'C0001', name: 'Acme Retail' });

    expect(cart.hold(new Date('2026-10-01T05:00:00Z'))).toBe(true);
    expect(cart.lines()).toEqual([]);
    expect(cart.customer()).toBeNull();
    expect(cart.holds().map((x) => x.label)).toEqual(['Acme Retail']);

    cart.add(coke);
    cart.recall(cart.holds()[0].id, new Date('2026-10-01T05:01:00Z'));

    expect(cart.lines().map((x) => x.product.id)).toEqual(['momo']);
    expect(cart.customer()?.name).toBe('Acme Retail');
    expect(cart.holds().length).toBe(1);
    expect(cart.holds()[0].lines.map((x) => x.product.id)).toEqual(['coke']);
  });

  it('holds nothing when the cart is empty', () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new MemoryStorage());
    expect(cart.hold()).toBe(false);
    expect(cart.holds()).toEqual([]);
  });

  it('keeps working when storage refuses every read and write', () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new BrokenStorage());

    cart.add(momo);
    cart.hold();

    expect(cart.holds().length).toBe(1);
  });

  it('forgets the session’s cart and holds on close', () => {
    const storage = new MemoryStorage();
    const cart = new PosCart(till(), ORG, 'ses-1', storage);
    cart.add(momo);
    cart.hold();

    cart.forget();

    expect(cart.holds()).toEqual([]);
    expect(storage.length).toBe(0);
  });
});
