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
    serviceChargeRate: 10, packagingFeeBasis: 'None', packagingFeeValue: 0,
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
    printCreditNote: true,
    canRefund: true,
    canDiscount: true,
    canChangeRate: true,
    ...overrides,
  };
}

const momo: PosProduct = {
  id: 'momo', code: 'P0001', name: 'Chicken Momo', categoryId: 'food', type: 'Service', vatRate: 'ThirteenPercentVat',
  rate: 200, serviceChargeApplicable: true, packagingFeeApplicable: true, batchTracking: false, serialTracking: false, barcode: null,
  units: [{ unitId: 'plate', shortName: 'plt', conversionRate: 1, rate: 200 }],
};

const coke: PosProduct = {
  id: 'coke', code: 'P0002', name: 'Coke 250ml', categoryId: 'drinks', type: 'Goods', vatRate: 'ThirteenPercentVat',
  rate: 60, serviceChargeApplicable: false, packagingFeeApplicable: true, batchTracking: false, serialTracking: false, barcode: '8901',
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

  // Phase 64 -- the vendor's restaurant till charges it on Dine In only (248.60 vs 226.00 for the Momo),
  // and so does the server's PosServiceCharge.RateFor; the cart follows, or the screen's total is not the server's.
  it('charges no service charge on a Delivery bill, and charges it again when switched back', () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new MemoryStorage());
    cart.add(momo);

    cart.setOrderType('Delivery');
    expect(cart.bill().serviceCharge).toBe(0);
    expect(cart.bill().grandTotal).toBe(226);

    cart.setOrderType('Retail');
    expect(cart.bill().serviceCharge).toBe(20);
  });

  it('re-prices a line at the catalogue rate of the unit it is changed to, and sends that unit', () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new MemoryStorage());
    cart.add(coke);
    const key = cart.lines()[0].key;

    cart.setUnit(key, 'crate');

    expect(cart.lines()[0].rate).toBe(1200);
    expect(cart.toSaleLines()[0]).toEqual({
      productId: 'coke', quantity: 1, rate: 1200, vatRate: null, discountPct: 0, discountPerUnit: 0, unitId: 'crate', serialNumbers: null,
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

describe('PosCart packaging fee (phase 70)', () => {
  const feeTill = (overrides: Partial<PosTill> = {}) =>
    till('loc-1', { packagingFeeBasis: 'PerUnit', packagingFeeValue: 20, ...overrides });

  it('charges a parcel the fee per unit, inside the VAT base, and no service charge', () => {
    const cart = new PosCart(feeTill(), ORG, 'ses-1', new MemoryStorage());
    cart.orderType.set('Delivery');

    cart.add(momo);
    cart.add(momo);

    // 400 + 40 fee + 13% of 440, as PackagingFeeHandlerTests bills the same sale on the server.
    expect(cart.bill().serviceCharge).toBe(0);
    expect(cart.bill().packagingFee).toBe(40);
    expect(cart.bill().vat).toBe(57.2);
    expect(cart.bill().grandTotal).toBe(497);
  });

  it('charges no fee on a counter sale, and none on a product exempted from it', () => {
    const counter = new PosCart(feeTill(), ORG, 'ses-1', new MemoryStorage());
    counter.add(momo);
    expect(counter.bill().packagingFee).toBe(0);

    const parcel = new PosCart(feeTill(), ORG, 'ses-2', new MemoryStorage());
    parcel.orderType.set('Delivery');
    parcel.add({ ...coke, packagingFeeApplicable: false });
    parcel.add(momo);
    expect(parcel.bill().lines.map((x) => x.packagingFee)).toEqual([0, 20]);
  });

  it('takes a percent fee on the amount after the bill discount', () => {
    const cart = new PosCart(feeTill({ packagingFeeBasis: 'Percent', packagingFeeValue: 10 }), ORG, 'ses-1', new MemoryStorage());
    cart.orderType.set('Delivery');
    cart.discountPct.set(5);

    cart.add(momo);

    // 200 less 5% is 190; 10% of that is 19.
    expect(cart.bill().packagingFee).toBe(19);
  });

  it('reads a product stored before the phase, with no flag, as paying the fee', () => {
    const cart = new PosCart(feeTill(), ORG, 'ses-1', new MemoryStorage());
    cart.orderType.set('Delivery');
    const stored: Partial<PosProduct> = { ...momo };
    delete stored.packagingFeeApplicable;

    cart.add(stored as PosProduct);

    expect(cart.bill().packagingFee).toBe(20);
  });
});

// Phase 91 -- a serial-tracked product sells unit by unit.
const phone: PosProduct = {
  id: 'phone', code: 'P0091', name: 'P91 Phone', categoryId: 'phones', type: 'Goods', vatRate: 'ThirteenPercentVat',
  rate: 10000, serviceChargeApplicable: false, packagingFeeApplicable: true, batchTracking: false, serialTracking: true,
  barcode: 'PHONE-BOX',
  units: [
    { unitId: 'pc', shortName: 'pc', conversionRate: 1, rate: 10000 },
    { unitId: 'box', shortName: 'box', conversionRate: 10, rate: 100000 },
  ],
};

describe('PosCart serials (phase 91)', () => {
  it('keeps one line per serial product, grows it to the units named, and sends them', () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new MemoryStorage());

    expect(cart.addSerial(phone, 'IMEI-1')).toBe(true);
    expect(cart.addSerial(phone, ' IMEI-2 ')).toBe(true);

    expect(cart.lines().length).toBe(1);
    expect(cart.lines()[0].quantity).toBe(2);
    expect(cart.lines()[0].serialNumbers).toEqual(['IMEI-1', 'IMEI-2']);
    expect(cart.serialShortfall()).toBeNull();
    expect(cart.toSaleLines()[0].serialNumbers).toEqual(['IMEI-1', 'IMEI-2']);
  });

  it('refuses a unit already in the cart, whatever its case', () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new MemoryStorage());

    cart.addSerial(phone, 'IMEI-1');

    expect(cart.addSerial(phone, 'imei-1')).toBe(false);
    expect(cart.lines()[0].quantity).toBe(1);
  });

  it('names a shortfall until every unit is named, and a tap adds a unit to the same line, not a second line', () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new MemoryStorage());

    const line = cart.add(phone)!;
    cart.setLineDiscount(line.key, 5);
    cart.add(phone);

    expect(cart.lines().length).toBe(1);
    expect(cart.serialShortfall()).toEqual({ line: cart.lines()[0], named: 0 });

    cart.addSerial(phone, 'IMEI-1');
    expect(cart.serialShortfall()!.named).toBe(1);
    cart.addSerial(phone, 'IMEI-2');
    expect(cart.serialShortfall()).toBeNull();
  });

  it('counts a serial line in whole primary units, and fewer units name fewer serials', () => {
    const cart = new PosCart(till(), ORG, 'ses-1', new MemoryStorage());
    cart.addSerial(phone, 'IMEI-1');
    cart.addSerial(phone, 'IMEI-2');
    const key = cart.lines()[0].key;

    cart.setQuantity(key, 1.5);
    expect(cart.lines()[0].quantity).toBe(2);

    cart.setUnit(key, 'box');
    expect(cart.lines()[0].unitId).toBe('pc');

    cart.setQuantity(key, 1);
    expect(cart.lines()[0].serialNumbers).toEqual(['IMEI-1']);

    cart.removeSerial(key, 'IMEI-1');
    expect(cart.serialShortfall()).toEqual({ line: cart.lines()[0], named: 0 });
  });

  it('keeps the serials through a hold and a recall, and a stored cart from before the phase reads as untracked', () => {
    const storage = new MemoryStorage();
    const cart = new PosCart(till(), ORG, 'ses-1', storage);
    cart.addSerial(phone, 'IMEI-1');
    cart.hold();
    cart.recall(cart.holds()[0].id);

    expect(cart.lines()[0].serialNumbers).toEqual(['IMEI-1']);

    // Phase 62's shape, which knew nothing of serials.
    storage.setItem(cartStorageKey(ORG, 'loc-1', 'ses-2'), JSON.stringify({
      version: 1, discountPct: 0, customer: null, orderType: 'Retail', holds: [],
      lines: [{ key: 1, product: { ...coke, serialTracking: undefined }, unitId: 'pc', quantity: 2, rate: 60, discountPct: 0 }],
    }));
    const old = new PosCart(till(), ORG, 'ses-2', storage);
    expect(old.serialShortfall()).toBeNull();
    expect(old.toSaleLines()[0].serialNumbers).toBeNull();
  });
});
