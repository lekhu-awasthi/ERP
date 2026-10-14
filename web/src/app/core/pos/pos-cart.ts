import { computed, signal } from '@angular/core';

import { BillFigures, BillLineInput, computeBill } from './pos-bill';
import { PackagingFeeBasis, PosProduct, PosProductUnit, PosTab, PosTill } from './pos.models';

/** The part of a product a cart line keeps: enough to show it, re-price it and send it. */
export interface CartProduct {
  id: string;
  code: string;
  name: string;
  vatRate: PosProduct['vatRate'];
  serviceChargeApplicable: boolean;
  /** Phase 70. A cart stored before the phase lacks it; absent reads as the product default, on. */
  packagingFeeApplicable: boolean;
  units: PosProductUnit[];
  /** Phase 91 -- each unit sold names its serial. A cart stored before the phase lacks it: not tracked. */
  serialTracking?: boolean;
}

/** Phase 70 -- the packaging fee terms a line is priced with. */
export interface CartPackagingFee {
  basis: PackagingFeeBasis;
  value: number;
}

const NO_PACKAGING_FEE: CartPackagingFee = { basis: 'None', value: 0 };

export interface CartLine {
  key: number;
  product: CartProduct;
  /** Always one of `product.units`; the primary unit's id when the line is in the primary unit. */
  unitId: string;
  quantity: number;
  /** Per unit, exclusive of VAT; the unit's catalogue rate until the cashier changes it. */
  rate: number;
  discountPct: number;
  /** Phase 74 -- rupees off each unit, never beside `discountPct`. A cart stored before the phase lacks it: none. */
  discountPerUnit: number;
  /** Phase 91 -- the units of a serial-tracked product, one per unit sold; the server refuses a sale whose count
   * is not the quantity. A cart stored before the phase lacks it: none. */
  serialNumbers?: string[];
}

/** Phase 91 -- a serial-tracked line whose serials do not yet match its quantity. */
export interface SerialShortfall {
  line: CartLine;
  named: number;
}

/** The customer a bill is for. Null on the cart means the walk-in. */
export interface CartCustomer {
  id: string;
  code: string;
  name: string;
}

/** A parked cart (Decision C): everything needed to put it back exactly as it was. */
export interface HeldCart {
  id: string;
  heldAt: string;
  label: string;
  lines: CartLine[];
  discountPct: number;
  /** Phase 74 -- rupees off the bill; absent on a cart held before the phase. */
  discountAmount?: number;
  customer: CartCustomer | null;
  orderType: PosTab | null;
}

interface StoredCart {
  version: 1;
  lines: CartLine[];
  discountPct: number;
  discountAmount?: number;
  customer: CartCustomer | null;
  orderType: PosTab | null;
  holds: HeldCart[];
}

/** Every key this class writes starts with this, so a sweep can find its own and nothing else's. */
export const CART_STORAGE_PREFIX = 'erp.pos.cart';

/**
 * Phase 62 -- one till's cart, its holds, and how both survive a reload.
 *
 * <p><b>Where a hold lives (Decision C): in this browser, never on the server.</b> A Retail hold
 * parks a cart so the next customer can be served, and is recalled minutes later at the same
 * counter. The vendor makes each hold an Approved, numbered Sales Order (SO0004 in phase 59's
 * service): a document in the ledger's numbering for a cart nobody bought. Phase 64's `PosOrder`
 * is where a server-side open order belongs, with its per-line counters, and building a minimal one
 * now would design it twice. So a hold reserves no stock, takes no number and posts nothing, and it
 * is lost with the browser's storage -- which is the honest weight of a parked basket.</p>
 *
 * <p><b>Why the cart cannot leak across locations (the vendor's defect 8).</b> Its key is
 * organization × location × <i>session</i>, and a session is one cashier at one location. The till
 * builds the cart only after it knows the session, so a cart saved at one till is not even looked
 * for at another; and a new session sweeps the keys its earlier sessions at the same till left
 * behind, so a closed drawer's holds do not resurface tomorrow.</p>
 *
 * <p><b>Storage is a convenience, not a record.</b> Every read and write is guarded: a private
 * window, a full quota or a blocked storage API leaves a cart that works and simply does not
 * survive a reload.</p>
 */
export class PosCart {
  private nextKey = 1;

  readonly lines = signal<CartLine[]>([]);
  readonly discountPct = signal(0);
  /** Phase 74 -- the bill's Amount discount, rupees, spread over its lines; never beside `discountPct`. */
  readonly discountAmount = signal(0);
  readonly customer = signal<CartCustomer | null>(null);
  readonly orderType = signal<PosTab | null>(null);
  readonly holds = signal<HeldCart[]>([]);

  /** The bill as the server will compute it -- see pos-bill.ts. */
  readonly bill = computed<BillFigures>(() =>
    computeBill(
      this.lines().map((line) => this.toBillLine(line)),
      this.discountPct(),
      this.till.roundOffEnabled,
      this.discountAmount(),
    ),
  );

  readonly itemCount = computed(() => this.lines().reduce((sum, line) => sum + line.quantity, 0));

  /**
   * Phase 91 -- the first serial-tracked line still short of (or past) its serials: the sale cannot be taken until each
   * unit handed over is named, which is what the server checks (and the vendor's cart blocks checkout on too).
   */
  readonly serialShortfall = computed<SerialShortfall | null>(() => {
    const line = this.lines().find((x) => x.product.serialTracking && (x.serialNumbers ?? []).length !== x.quantity);
    return line ? { line, named: (line.serialNumbers ?? []).length } : null;
  });

  private readonly storageKey: string;

  constructor(
    private readonly till: PosTill,
    organizationId: string,
    sessionId: string,
    private readonly storage: Storage | null = safeLocalStorage(),
  ) {
    this.storageKey = cartStorageKey(organizationId, till.locationId, sessionId);
    this.orderType.set(till.defaultTab);
    this.sweepEarlierSessions(organizationId);
    this.restore();
  }

  /**
   * The service charge rate a line carries: the location's, when it charges and the product is flagged,
   * and never on a Take Away or Delivery bill -- service charge is a dine-in charge (phase 64's live read;
   * the server's `PosServiceCharge.RateFor`, which this mirrors, so the bill on screen stays the server's).
   */
  serviceChargeRateOf(product: CartProduct): number {
    const orderType = this.orderType();
    if (orderType === 'TakeAway' || orderType === 'Delivery') {
      return 0;
    }
    return this.till.serviceChargeEnabled && product.serviceChargeApplicable ? this.till.serviceChargeRate : 0;
  }

  /**
   * Phase 70 -- the packaging fee a line carries: the location's, when the bill is a parcel (Take Away or
   * Delivery) and the product is not exempt -- the server's `PosPackagingFee.For`, which this mirrors, so the
   * bill on screen stays the server's. The mirror of {@link serviceChargeRateOf}, which charges only what is
   * not a parcel.
   */
  packagingFeeOf(product: CartProduct): CartPackagingFee {
    const orderType = this.orderType();
    const parcel = orderType === 'TakeAway' || orderType === 'Delivery';
    return parcel && product.packagingFeeApplicable !== false && this.till.packagingFeeBasis !== 'None'
      ? { basis: this.till.packagingFeeBasis, value: this.till.packagingFeeValue }
      : NO_PACKAGING_FEE;
  }

  /**
   * Adds one of a product in its primary unit, or one more onto a line already holding exactly that. Phase 91 -- a
   * serial-tracked product has one line, whatever its price or discount, which its serials are named on; the line
   * returned is the one that grew, so the till can open its serial picker.
   */
  add(product: PosProduct | CartProduct): CartLine | null {
    const kept = toCartProduct(product);
    const primary = kept.units[0];
    const existing = this.lines().find(
      (x) =>
        x.product.id === kept.id &&
        (kept.serialTracking ||
          (x.unitId === primary.unitId && x.rate === primary.rate && x.discountPct === 0 && !x.discountPerUnit)),
    );

    if (existing) {
      this.update(existing.key, { quantity: existing.quantity + 1 });
      return this.lines().find((x) => x.key === existing.key) ?? null;
    }

    const line: CartLine = {
      key: this.nextKey++,
      product: kept,
      unitId: primary.unitId,
      quantity: 1,
      rate: primary.rate,
      discountPct: 0,
      discountPerUnit: 0,
      serialNumbers: [],
    };
    this.lines.set([...this.lines(), line]);
    this.persist();
    return line;
  }

  /**
   * Phase 91 -- names one unit of a serial-tracked product, on its line (made if there is none). The quantity grows to
   * the serials named when they outnumber it, so ticking three phones sells three. False when the serial is already in
   * the cart: one unit cannot be sold twice.
   */
  addSerial(product: PosProduct | CartProduct, serialNo: string): boolean {
    const serial = serialNo.trim();
    if (!serial || this.hasSerial(serial)) {
      return false;
    }

    const kept = toCartProduct(product);
    const existing = this.lines().find((x) => x.product.id === kept.id);
    const named = existing?.serialNumbers ?? [];
    const serials = [...named, serial];

    if (!existing) {
      const line = this.add(kept)!;
      this.update(line.key, { serialNumbers: serials, quantity: 1 });
      return true;
    }

    this.update(existing.key, { serialNumbers: serials, quantity: Math.max(existing.quantity, serials.length) });
    return true;
  }

  /** Phase 91 -- takes one unit's serial off its line; the quantity stays, so the cashier names another or lowers it. */
  removeSerial(key: number, serialNo: string): void {
    const line = this.lines().find((x) => x.key === key);
    if (!line) return;
    this.update(key, { serialNumbers: (line.serialNumbers ?? []).filter((x) => x !== serialNo) });
  }

  /** Phase 91 -- whether a unit is already on any line of this cart (serials compare as the server's do, ignoring case). */
  hasSerial(serialNo: string): boolean {
    const wanted = serialNo.trim().toLowerCase();
    return this.lines().some((x) => (x.serialNumbers ?? []).some((s) => s.toLowerCase() === wanted));
  }

  setQuantity(key: number, quantity: number): void {
    if (!Number.isFinite(quantity) || quantity <= 0) {
      return;
    }

    // Phase 91 -- a serial is one whole unit: a serial-tracked line counts in whole units, and fewer units name fewer
    // serials (the last named are the ones put back).
    const line = this.lines().find((x) => x.key === key);
    if (line?.product.serialTracking) {
      if (quantity !== Math.trunc(quantity)) return;
      this.update(key, { quantity, serialNumbers: (line.serialNumbers ?? []).slice(0, quantity) });
      return;
    }

    this.update(key, { quantity });
  }

  /** Changing the unit re-prices the line at that unit's catalogue rate, as the ERP's line grid does. Phase 91 -- a
   * serial names one primary unit, so a serial-tracked line stays in it. */
  setUnit(key: number, unitId: string): void {
    const line = this.lines().find((x) => x.key === key);
    const unit = line?.product.units.find((u) => u.unitId === unitId);
    if (!line || !unit || line.product.serialTracking) {
      return;
    }

    this.update(key, { unitId: unit.unitId, rate: unit.rate });
  }

  setRate(key: number, rate: number): void {
    if (!Number.isFinite(rate) || rate < 0) {
      return;
    }

    this.update(key, { rate });
  }

  setLineDiscount(key: number, discountPct: number): void {
    if (!Number.isFinite(discountPct) || discountPct < 0 || discountPct > 100) {
      return;
    }

    this.update(key, { discountPct, discountPerUnit: 0 });
  }

  /** Phase 74 -- rupees off each unit of the line, replacing a percentage; never more than its rate. */
  setLineDiscountPerUnit(key: number, discountPerUnit: number): void {
    const line = this.lines().find((x) => x.key === key);
    if (!line || !isPaisa(discountPerUnit) || discountPerUnit > line.rate) {
      return;
    }

    this.update(key, { discountPerUnit, discountPct: 0 });
  }

  setBillDiscount(discountPct: number): void {
    if (!Number.isFinite(discountPct) || discountPct < 0 || discountPct > 100) {
      return;
    }

    this.discountPct.set(discountPct);
    this.discountAmount.set(0);
    this.persist();
  }

  /** Phase 74 -- rupees off the whole bill, replacing a percentage. More than the bill is the server's 400, and the
   * till says so before it is sent. */
  setBillDiscountAmount(discountAmount: number): void {
    if (!isPaisa(discountAmount)) {
      return;
    }

    this.discountAmount.set(discountAmount);
    this.discountPct.set(0);
    this.persist();
  }

  setCustomer(customer: CartCustomer | null): void {
    this.customer.set(customer);
    this.persist();
  }

  setOrderType(orderType: PosTab): void {
    this.orderType.set(orderType);
    this.persist();
  }

  remove(key: number): void {
    this.lines.set(this.lines().filter((x) => x.key !== key));
    this.persist();
  }

  /** Empties the cart for the next customer, keeping the holds. */
  clear(): void {
    this.lines.set([]);
    this.discountPct.set(0);
    this.discountAmount.set(0);
    this.customer.set(null);
    this.orderType.set(this.till.defaultTab);
    this.persist();
  }

  /** Parks the current cart and starts an empty one. Returns false when there is nothing to park. */
  hold(now: Date = new Date()): boolean {
    if (this.lines().length === 0) {
      return false;
    }

    const customer = this.customer();
    const held: HeldCart = {
      id: `${now.getTime()}-${this.nextKey++}`,
      heldAt: now.toISOString(),
      label: customer ? customer.name : `${this.lines().length} line(s), ${this.bill().grandTotal.toFixed(2)}`,
      lines: this.lines(),
      discountPct: this.discountPct(),
      discountAmount: this.discountAmount(),
      customer,
      orderType: this.orderType(),
    };

    this.holds.set([...this.holds(), held]);
    this.clear();
    return true;
  }

  /**
   * Puts a held cart back. The cart in progress, if any, is parked first rather than lost -- a
   * recall never discards anything the cashier did not ask to discard.
   */
  recall(id: string, now: Date = new Date()): void {
    const held = this.holds().find((x) => x.id === id);
    if (!held) {
      return;
    }

    this.holds.set(this.holds().filter((x) => x.id !== id));
    this.hold(now);

    this.lines.set(held.lines.map((line) => ({ ...line, discountPerUnit: line.discountPerUnit ?? 0, key: this.nextKey++ })));
    this.discountPct.set(held.discountPct);
    this.discountAmount.set(held.discountAmount ?? 0);
    this.customer.set(held.customer);
    this.orderType.set(held.orderType ?? this.till.defaultTab);
    this.persist();
  }

  discardHold(id: string): void {
    this.holds.set(this.holds().filter((x) => x.id !== id));
    this.persist();
  }

  /** What the sale command is sent as its lines. Rates are already exclusive of VAT. */
  toSaleLines(): {
    productId: string;
    quantity: number;
    rate: number;
    vatRate: null;
    discountPct: number;
    discountPerUnit: number;
    unitId: string | null;
    serialNumbers: string[] | null;
  }[] {
    return this.lines().map((line) => ({
      productId: line.product.id,
      quantity: line.quantity,
      rate: line.rate,
      // The product's own rate, which the server reads itself (phase 61): the till never overrides it.
      vatRate: null,
      discountPct: line.discountPct,
      discountPerUnit: line.discountPerUnit,
      // The primary unit is sent as null, which is what every ERP form sends for it.
      unitId: line.unitId === line.product.units[0]?.unitId ? null : line.unitId,
      // Phase 91 -- the units handed over, for a serial-tracked product.
      serialNumbers: line.product.serialTracking ? (line.serialNumbers ?? []) : null,
    }));
  }

  /** Forgets this session's cart and holds, as a close does. */
  forget(): void {
    this.lines.set([]);
    this.holds.set([]);
    try {
      this.storage?.removeItem(this.storageKey);
    } catch {
      // Storage is a convenience; nothing to undo.
    }
  }

  private toBillLine(line: CartLine): BillLineInput {
    return {
      quantity: line.quantity,
      rate: line.rate,
      vatRate: line.product.vatRate,
      discountPct: line.discountPct,
      discountPerUnit: line.discountPerUnit,
      serviceChargeRate: this.serviceChargeRateOf(line.product),
      packagingFeeBasis: this.packagingFeeOf(line.product).basis,
      packagingFeeValue: this.packagingFeeOf(line.product).value,
    };
  }

  private update(key: number, patch: Partial<CartLine>): void {
    this.lines.set(this.lines().map((x) => (x.key === key ? { ...x, ...patch } : x)));
    this.persist();
  }

  private persist(): void {
    const stored: StoredCart = {
      version: 1,
      lines: this.lines(),
      discountPct: this.discountPct(),
      discountAmount: this.discountAmount(),
      customer: this.customer(),
      orderType: this.orderType(),
      holds: this.holds(),
    };

    try {
      this.storage?.setItem(this.storageKey, JSON.stringify(stored));
    } catch {
      // A full quota or a blocked storage API: the cart still works, it just will not survive a reload.
    }
  }

  private restore(): void {
    let raw: string | null = null;
    try {
      raw = this.storage?.getItem(this.storageKey) ?? null;
    } catch {
      return;
    }

    if (!raw) {
      return;
    }

    try {
      const stored = JSON.parse(raw) as StoredCart;
      if (stored.version !== 1) {
        return;
      }

      this.lines.set(stored.lines.map((line) => ({ ...line, discountPerUnit: line.discountPerUnit ?? 0, key: this.nextKey++ })));
      this.discountPct.set(stored.discountPct ?? 0);
      this.discountAmount.set(stored.discountAmount ?? 0);
      this.customer.set(stored.customer ?? null);
      this.orderType.set(stored.orderType ?? this.till.defaultTab);
      this.holds.set(stored.holds ?? []);
    } catch {
      // Unreadable is the same as absent: start empty rather than fail the till.
    }
  }

  /** Removes what earlier sessions at this same till left behind; other tills' keys are not touched. */
  private sweepEarlierSessions(organizationId: string): void {
    const prefix = `${CART_STORAGE_PREFIX}:${organizationId}:${this.till.locationId}:`;

    try {
      const stale: string[] = [];
      for (let i = 0; i < (this.storage?.length ?? 0); i++) {
        const key = this.storage!.key(i);
        if (key && key.startsWith(prefix) && key !== this.storageKey) {
          stale.push(key);
        }
      }

      stale.forEach((key) => this.storage!.removeItem(key));
    } catch {
      // Nothing to sweep if storage cannot be read.
    }
  }
}

export function cartStorageKey(organizationId: string, locationId: string, sessionId: string): string {
  return `${CART_STORAGE_PREFIX}:${organizationId}:${locationId}:${sessionId}`;
}

/** How many carts this browser holds for a session -- what a close warns it will discard. */
export function storedHoldCount(
  organizationId: string,
  locationId: string,
  sessionId: string,
  storage: Storage | null = safeLocalStorage(),
): number {
  try {
    const raw = storage?.getItem(cartStorageKey(organizationId, locationId, sessionId));
    return raw ? ((JSON.parse(raw) as StoredCart).holds?.length ?? 0) : 0;
  } catch {
    return 0;
  }
}

/** Removes a session's cart and holds from this browser, as its close does. */
export function forgetStoredCart(
  organizationId: string,
  locationId: string,
  sessionId: string,
  storage: Storage | null = safeLocalStorage(),
): void {
  try {
    storage?.removeItem(cartStorageKey(organizationId, locationId, sessionId));
  } catch {
    // Storage is a convenience; nothing to undo.
  }
}

function toCartProduct(product: PosProduct | CartProduct): CartProduct {
  return {
    id: product.id,
    code: product.code,
    name: product.name,
    vatRate: product.vatRate,
    serviceChargeApplicable: product.serviceChargeApplicable,
    packagingFeeApplicable: product.packagingFeeApplicable !== false,
    units: product.units,
    serialTracking: product.serialTracking === true,
  };
}

/** Phase 74 -- an amount a till takes: zero or more, in whole paisa (the server's `PosCashAmount`). */
function isPaisa(value: number): boolean {
  return Number.isFinite(value) && value >= 0 && Number(value.toFixed(2)) === value;
}

function safeLocalStorage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null;
  }
}
