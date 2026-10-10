import { VatRate } from '../catalog/catalog.models';
import { PackagingFeeBasis } from './pos.models';

/**
 * Phase 62 -- the till's bill arithmetic: the client's copy of `InvoiceLine.CreatePos` and
 * `Invoice.ApplyRoundOff`.
 *
 * <p><b>Why the client computes a total at all.</b> The cashier takes notes against the figure on
 * screen before the server has seen the sale. If the screen said 633 and the server billed 632, the
 * difference would be a rupee of change or a rupee of credit on the walk-in, and phase 61 refuses
 * credit on the walk-in. So the two implementations are pinned to one table,
 * `pos-bill-cases.json`, read by `pos-bill.spec.ts` here and `PosBillSharedCasesTests` on the
 * server -- phase 26b's arrangement for the BS calendar.</p>
 *
 * <p><b>Why integers.</b> The server rounds decimals half away from zero at the paisa. A binary
 * float cannot: `1.005` is stored as 1.00499999…, so `Math.round(1.005 * 100) / 100` is 1.00 where
 * the server says 1.01. Every input is therefore scaled to an exact integer of millionths (the till
 * never enters more decimals than that) and the arithmetic is done in `bigint`, with one rounding
 * per figure exactly where the server rounds.</p>
 *
 * <p><b>Phase 70 -- the packaging fee</b> is taken beside the service charge, on the same rounded amount
 * (a percent) or per unit (a flat amount), and is inside the VAT base: `PosLineArithmetic.Compute`.
 * Its two fields are required, not optional -- an optional field is where a sweep silently stops
 * (phase 52), and a line that forgot them would be billed a different total than the server's.</p>
 *
 * <p><b>Phase 74 -- the Amount discounts</b>: a line's is taken off each unit before anything else, and a bill's is
 * spread over the lines by what each comes to, the residue on the largest (`PosLineArithmetic.Spread`), and taken
 * off each line's rounded amount before the service charge, the fee and VAT.</p>
 */

export interface BillLineInput {
  quantity: number;
  /** Per unit, exclusive of VAT. */
  rate: number;
  vatRate: VatRate;
  /** The line's own discount, percent. */
  discountPct: number;
  /**
   * Phase 74 -- the line's Amount discount, rupees off each unit; never beside `discountPct`. Required, like the fee's
   * two fields: an optional field is where a sweep silently stops.
   */
  discountPerUnit: number;
  /** Percent; zero unless the location charges it and the product carries the flag. */
  serviceChargeRate: number;
  /** Phase 70 -- 'None' unless the line is a parcel of a product not exempted and the location charges a fee. */
  packagingFeeBasis: PackagingFeeBasis;
  /** A percent for 'Percent', rupees a unit for 'PerUnit'. */
  packagingFeeValue: number;
}

export interface BillLineFigures {
  amount: number;
  serviceCharge: number;
  packagingFee: number;
  vat: number;
  total: number;
}

export interface BillFigures {
  lines: BillLineFigures[];
  /** Phase 74 -- the bill's Amount discount as spread: what was asked for, or the whole bill if that was more. */
  billDiscount: number;
  subTotal: number;
  serviceCharge: number;
  packagingFee: number;
  vat: number;
  roundOff: number;
  grandTotal: number;
}

const MICRO = 1_000_000n;
const HUNDRED_PERCENT = 100n * MICRO;

/** An input number as an exact count of millionths. */
function micro(value: number): bigint {
  return BigInt(Math.round(value * 1_000_000));
}

/** n / d rounded half away from zero, for d > 0. */
function divideHalfAway(n: bigint, d: bigint): bigint {
  if (n < 0n) {
    return -divideHalfAway(-n, d);
  }

  return (2n * n + d) / (2n * d);
}

function vatPercent(rate: VatRate): bigint {
  return rate === 'ThirteenPercentVat' ? 13n : 0n;
}

function rupees(paisa: bigint): number {
  return Number(paisa) / 100;
}

/** Phase 70 -- a line's packaging fee in paisa: a percent of its rounded amount, or rupees for each unit. */
function packagingFeeOf(line: BillLineInput, amountPaisa: bigint): bigint {
  switch (line.packagingFeeBasis) {
    case 'Percent':
      return divideHalfAway(amountPaisa * micro(line.packagingFeeValue), HUNDRED_PERCENT);
    case 'PerUnit':
      return divideHalfAway(micro(line.quantity) * micro(line.packagingFeeValue) * 100n, MICRO * MICRO);
    default:
      return 0n;
  }
}

/**
 * Phase 74 -- a bill's amount in paisa as each line's share, in proportion to what each comes to, the residue on the
 * largest line (the first of equals): the server's `PosLineArithmetic.Spread`. An amount over the bill is spread as the
 * whole bill; the server refuses it, and the till says so before it is sent.
 */
function spread(amount: bigint, bases: readonly bigint[]): bigint[] {
  const total = bases.reduce((sum, x) => sum + x, 0n);
  const whole = amount > total ? total : amount;
  if (whole <= 0n || total === 0n) {
    return bases.map(() => 0n);
  }

  const shares = bases.map((base) => divideHalfAway(whole * base, total));
  let largest = 0;
  bases.forEach((base, i) => {
    if (base > bases[largest]) {
      largest = i;
    }
  });
  shares[largest] += whole - shares.reduce((sum, x) => sum + x, 0n);
  return shares;
}

/** The whole bill, line by line, as the server will bill it. Phase 74 -- `billDiscountAmount` is rupees off the bill,
 * never beside `billDiscountPct`. */
export function computeBill(
  lines: readonly BillLineInput[],
  billDiscountPct: number,
  roundOff: boolean,
  billDiscountAmount: number,
): BillFigures {
  const header = HUNDRED_PERCENT - micro(billDiscountPct);
  let total = 0n;

  // quantity × (rate − its amount off) × (1 − line discount) × (1 − bill discount), to the paisa: what a bill's amount
  // is spread by, and taken from.
  const nets = lines.map((line) => {
    const numerator =
      micro(line.quantity) * (micro(line.rate) - micro(line.discountPerUnit)) * (HUNDRED_PERCENT - micro(line.discountPct)) * header * 100n;
    const denominator = MICRO * MICRO * HUNDRED_PERCENT * HUNDRED_PERCENT;
    return divideHalfAway(numerator, denominator);
  });
  const shares = spread(micro(billDiscountAmount) / 10_000n, nets);

  const figures = lines.map((line, i) => {
    const amount = nets[i] - shares[i];

    // Service charge on the rounded amount, the packaging fee beside it (phase 70), and VAT on the
    // amount plus both -- each rounded to the paisa as the server does (phase 61 Decision L).
    const serviceCharge = divideHalfAway(amount * micro(line.serviceChargeRate), HUNDRED_PERCENT);
    const packagingFee = packagingFeeOf(line, amount);
    const vat = divideHalfAway((amount + serviceCharge + packagingFee) * vatPercent(line.vatRate), 100n);
    const lineTotal = amount + serviceCharge + packagingFee + vat;
    total += lineTotal;

    return { amount, serviceCharge, packagingFee, vat, lineTotal };
  });

  // To the nearest rupee, half away from zero (phase 61 Decision B).
  const rounded = roundOff ? divideHalfAway(total, 100n) * 100n : total;

  return {
    lines: figures.map((x) => ({
      amount: rupees(x.amount),
      serviceCharge: rupees(x.serviceCharge),
      packagingFee: rupees(x.packagingFee),
      vat: rupees(x.vat),
      total: rupees(x.lineTotal),
    })),
    billDiscount: rupees(shares.reduce((sum, x) => sum + x, 0n)),
    subTotal: rupees(figures.reduce((sum, x) => sum + x.amount, 0n)),
    serviceCharge: rupees(figures.reduce((sum, x) => sum + x.serviceCharge, 0n)),
    packagingFee: rupees(figures.reduce((sum, x) => sum + x.packagingFee, 0n)),
    vat: rupees(figures.reduce((sum, x) => sum + x.vat, 0n)),
    roundOff: rupees(rounded - total),
    grandTotal: rupees(rounded),
  };
}

/** A money figure as whole paisa, for comparing and adding tenders without float drift. */
export function toPaisa(value: number): number {
  return Math.round(value * 100);
}

/** The inverse of {@link toPaisa}. */
export function fromPaisa(paisa: number): number {
  return paisa / 100;
}
