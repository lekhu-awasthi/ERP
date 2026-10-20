import { ProductType, VatRate } from '../catalog/catalog.models';
import { PaymentModeKind } from '../configuration/configuration.models';
import { PosMode } from '../organizations/organizations.models';

// Phase 60 -- the ERP side of point-of-sale configuration (docs/phase-60-status.md). Mirrors
// Application.Pos's DTOs; enums arrive as strings (JsonStringEnumConverter).

/** Phase 70 -- how a location charges for packing a parcel. Mirrors Domain.Sales.PackagingFeeBasis. */
export type PackagingFeeBasis = 'None' | 'Percent' | 'PerUnit';

export const PACKAGING_FEE_BASIS_LABELS: Readonly<Record<PackagingFeeBasis, string>> = {
  None: 'No packaging fee',
  Percent: 'A percent of each parcelled line',
  PerUnit: 'A flat amount per unit',
};

/** Which tab a till opens on. Mirrors Domain.Pos.PosTab. */
export type PosTab = 'Retail' | 'DineIn' | 'TakeAway' | 'Delivery';

/** Phase 74 -- how a till discount is given, the vendor's Percent | Amount toggle. On a line an amount is rupees
 * off each unit; on a bill or an order, rupees off the whole. */
export type PosDiscountKind = 'Percent' | 'Amount';

export const POS_TAB_LABELS: Readonly<Record<PosTab, string>> = {
  Retail: 'Retail',
  DineIn: 'Dine In',
  TakeAway: 'Take Away',
  Delivery: 'Delivery',
};

export const POS_MODE_LABELS: Readonly<Record<PosMode, string>> = {
  None: 'No till',
  Retail: 'Retail',
  Restaurant: 'Bar / Restaurant',
};

export interface PosWalkInCustomer {
  id: string;
  code: string;
  name: string;
}

export interface PosLocationSummary {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
  isHeadOffice: boolean;
  posMode: PosMode;
  hasSettings: boolean;
  linkedPaymentModeCount: number;
}

export interface PosConfiguration {
  posRetailEnabled: boolean;
  posRestaurantEnabled: boolean;
  walkInCustomer: PosWalkInCustomer | null;
  locations: PosLocationSummary[];
}

export interface PosLocationSettings {
  locationId: string;
  locationCode: string;
  locationName: string;
  locationIsActive: boolean;
  posMode: PosMode;
  /** False while the location reads as the defaults and has never been saved. */
  isSaved: boolean;
  serviceChargeEnabled: boolean;
  serviceChargeRate: number;
  serviceChargeAccountId: string | null;
  /** Phase 68 -- whether food parcelled from a dine-in line keeps its service charge. */
  serviceChargeOnTakeAway: boolean;
  /** Phase 70 -- the packaging fee on every parcel; the value is a percent or rupees a unit, per the basis. */
  packagingFeeBasis: PackagingFeeBasis;
  packagingFeeValue: number;
  packagingFeeAccountId: string | null;
  roundOffEnabled: boolean;
  roundOffAccountId: string | null;
  cashVerificationRequired: boolean;
  denominations: number[];
  defaultTab: PosTab | null;
  effectiveDefaultTab: PosTab | null;
  availableTabs: PosTab[];
  printEstimateBill: boolean;
  printInvoice: boolean;
  printCreditNote: boolean;
  printKot: boolean;
  /** Phase 62 -- the Tax Officer's permission to issue abbreviated tax invoices (VAT Rules Rule 18). */
  abbreviatedTaxInvoiceEnabled: boolean;
  paymentModeIds: string[];
}

export interface UpdatePosLocationSettingsRequest {
  serviceChargeEnabled: boolean;
  serviceChargeRate: number;
  serviceChargeAccountId: string | null;
  /** Phase 68 -- whether food parcelled from a dine-in line keeps its service charge. */
  serviceChargeOnTakeAway: boolean;
  /** Phase 70 -- the packaging fee on every parcel; the value is a percent or rupees a unit, per the basis. */
  packagingFeeBasis: PackagingFeeBasis;
  packagingFeeValue: number;
  packagingFeeAccountId: string | null;
  roundOffEnabled: boolean;
  roundOffAccountId: string | null;
  cashVerificationRequired: boolean;
  denominations: number[];
  defaultTab: PosTab | null;
  printEstimateBill: boolean;
  printInvoice: boolean;
  printCreditNote: boolean;
  printKot: boolean;
  abbreviatedTaxInvoiceEnabled: boolean;
}

/** The vendor's default and "Reset to default" list. Mirrors PosLocationSettings.DefaultDenominations. */
export const DEFAULT_DENOMINATIONS: readonly number[] = [1000, 500, 100, 50, 20, 10, 5, 2, 1];

// ---- Phase 62 -- the till (docs/phase-62-status.md). Mirrors Application.Pos's till DTOs. ----------

/** One note value and how many of it -- a drawer count. Mirrors Domain.Pos.DenominationCount. */
export interface DenominationCount {
  value: number;
  count: number;
}

/** A till the cashier may open a drawer at, with their own open session there. */
export interface PosTillSummary {
  locationId: string;
  locationCode: string;
  locationName: string;
  posMode: PosMode;
  mySessionId: string | null;
  mySessionCode: string | null;
  mySessionOpenedAt: string | null;
}

export interface PosTillPaymentMode {
  id: string;
  name: string;
  kind: PaymentModeKind;
}

export interface PosTillCategory {
  id: string;
  name: string;
}

/** The till's own read of its location: what it acts on, under the cashier's key. */
export interface PosTill {
  locationId: string;
  locationCode: string;
  locationName: string;
  posMode: PosMode;
  availableTabs: PosTab[];
  defaultTab: PosTab | null;
  serviceChargeEnabled: boolean;
  serviceChargeRate: number;
  /** Phase 70 -- the location's packaging fee, which a Take Away or Delivery line carries (times the
   *  product's flag), priced by pos-bill.ts exactly as the server prices it. */
  packagingFeeBasis: PackagingFeeBasis;
  packagingFeeValue: number;
  roundOffEnabled: boolean;
  cashVerificationRequired: boolean;
  denominations: number[];
  printInvoice: boolean;
  abbreviatedTaxInvoiceEnabled: boolean;
  isVatRegistered: boolean;
  warehouseId: string | null;
  warehouseName: string | null;
  walkInCustomer: PosWalkInCustomer | null;
  paymentModes: PosTillPaymentMode[];
  categories: PosTillCategory[];
  /** Whether this cashier holds Sales.Invoice.Approve here, which credit needs (phase 61 Decision F). */
  canSellOnCredit: boolean;
  /** Phase 63 -- whether a refund's credit note prints itself (the location's toggle). */
  printCreditNote: boolean;
  /** Phase 63 -- whether this cashier holds Sales.CreditNote.Create and .Approve here, which a refund needs. */
  canRefund: boolean;
  /** Phase 72 -- whether this cashier may give a discount here. Phase 76 -- Sales.Invoice.TillDiscount, not Approve. */
  canDiscount: boolean;
  /**
   * Phase 76 -- whether this cashier holds Sales.Invoice.TillChangeRate here, which ringing a line up at any price
   * but the catalogue's needs (the vendor's Can Change Rate).
   */
  canChangeRate: boolean;
}

/** One unit a product sells in; `rate` is already exclusive of VAT for that unit. Primary first. */
export interface PosProductUnit {
  unitId: string;
  shortName: string;
  conversionRate: number;
  rate: number;
}

export interface PosProduct {
  id: string;
  code: string;
  name: string;
  categoryId: string;
  type: ProductType;
  vatRate: VatRate;
  /** The primary unit's rate, exclusive of VAT, after the tenant's price basis. */
  rate: number;
  serviceChargeApplicable: boolean;
  /** Phase 70 -- the product's half of the packaging-fee rule. */
  packagingFeeApplicable: boolean;
  batchTracking: boolean;
  serialTracking: boolean;
  barcode: string | null;
  units: PosProductUnit[];
  /** Phase 91 -- set when a scanned code matched no product but this in-stock serial of it. */
  scannedSerialNo?: string | null;
}

/** Phase 94 -- a batch in stock at the till's warehouse; `quantity` is in the product's primary unit. */
export interface PosProductBatch {
  batchNo: string;
  expiryDate: string | null;
  quantity: number;
}

export type PosSessionStatus = 'Open' | 'Closed';
export type PosCashMovementDirection = 'In' | 'Out';

export interface PosTenderTotal {
  paymentModeId: string;
  paymentModeName: string;
  kind: PaymentModeKind;
  amount: number;
}

/** The sales figures of one session (the X/Z report) -- PosSalesReader's one shape. */
export interface PosSalesSummary {
  salesCount: number;
  subTotal: number;
  serviceCharge: number;
  /** Phase 70. */
  packagingFee: number;
  vat: number;
  roundOff: number;
  grandTotal: number;
  tenders: PosTenderTotal[];
  tendered: number;
  change: number;
  settled: number;
  credit: number;
  cashSales: number;
  /** Phase 63 -- the refunds paid out in the same session or day. */
  refunds: PosRefundsSummary;
  /** Phase 63 -- grand total less the refunds' grand total. */
  netSales: number;
  /** Phase 66 -- sub-total and service charge on the lines with VAT, and on those without (the Sales
   *  Register's two buckets, split by its own rule). */
  taxable: number;
  nonTaxable: number;
  /** Phase 66 -- the round-off kept, net of the refunds': the one line between a till figure and the
   *  Sales Register, whose total is netSales less this. */
  netRoundOff: number;
  /** Phase 66 -- cash the drawers kept (cash sales less cash refunds), and credit still on accounts. */
  netCash: number;
  netCredit: number;
}

/** Phase 63 -- the refunds half of PosSalesReader's shape. */
export interface PosRefundsSummary {
  refundsCount: number;
  subTotal: number;
  serviceCharge: number;
  /** Phase 70. */
  packagingFee: number;
  vat: number;
  roundOff: number;
  grandTotal: number;
  payouts: PosTenderTotal[];
  paidOut: number;
  toAccount: number;
  cashRefunds: number;
  taxable: number;
  nonTaxable: number;
}

export interface PosCashMovement {
  id: string;
  direction: PosCashMovementDirection;
  amount: number;
  accountId: string;
  accountName: string;
  note: string | null;
  createdAt: string;
}

export interface PosSession {
  id: string;
  code: string;
  locationId: string;
  locationName: string;
  userId: string;
  userName: string;
  status: PosSessionStatus;
  openedAt: string;
  closedAt: string | null;
  cashAccountId: string;
  openingFloat: number;
  openingCount: DenominationCount[] | null;
  sales: PosSalesSummary;
  cashMovements: PosCashMovement[];
  cashIn: number;
  cashOut: number;
  expectedCash: number;
  countedCash: number | null;
  closingCount: DenominationCount[] | null;
  cashDifference: number | null;
  closingNote: string | null;
}

export interface OpenPosSessionRequest {
  locationId: string;
  openingAmount: number | null;
  denominations: DenominationCount[] | null;
}

export interface RecordPosCashMovementRequest {
  direction: PosCashMovementDirection;
  amount: number;
  accountId: string;
  note: string | null;
  overrideNegativeCashBalanceWarning: boolean;
}

export interface ClosePosSessionRequest {
  countedAmount: number | null;
  denominations: DenominationCount[] | null;
  note: string | null;
}

export interface PosSaleLineInput {
  productId: string;
  quantity: number;
  rate: number;
  vatRate: VatRate | null;
  discountPct: number;
  /** Phase 74 -- rupees off each unit, never beside `discountPct`. */
  discountPerUnit: number;
  unitId: string | null;
  /** Phase 91 -- the units handed over, one per unit, for a serial-tracked product; null otherwise. */
  serialNumbers: string[] | null;
}

export interface PosTenderInput {
  paymentModeId: string;
  amount: number;
}

/** Every field the endpoint's request record takes, so none binds to its default in silence (phase 27b). */
export interface CreatePosSaleRequest {
  sessionId: string;
  locationId: string;
  lines: PosSaleLineInput[];
  tenders: PosTenderInput[];
  changeAmount: number;
  contactId: string | null;
  warehouseId: string | null;
  orderType: PosTab | null;
  discountPct: number;
  /** Phase 74 -- rupees off the bill, spread over its lines; never beside `discountPct`. */
  discountAmount: number;
  overrideStockWarning: boolean;
  overrideCreditLimitWarning: boolean;
}

export interface CreatePosSaleResult {
  id: string;
  code: string;
  grandTotal: number;
  serviceCharge: number;
  /** Phase 70. */
  packagingFee: number;
  roundOff: number;
  tendered: number;
  changeAmount: number;
  creditAmount: number;
  isAbbreviatedTaxInvoice: boolean;
}

export type InvoiceStatusValue = 'Draft' | 'Approved' | 'Void';

/** One sale of a session, as the recent-sales list shows it. */
export interface PosSessionSale {
  invoiceId: string;
  code: string;
  status: InvoiceStatusValue;
  soldAt: string | null;
  customerName: string;
  isWalkIn: boolean;
  grandTotal: number;
  tendered: number;
  changeAmount: number;
  creditAmount: number;
  isAbbreviatedTaxInvoice: boolean;
  /** Above zero, the next print is a marked copy (phase-62-status.md Decision B). */
  printCount: number;
}

// ---- Phase 63: refunds --------------------------------------------------------------------------

/** A sale found by its number, for the refund screen. */
export interface PosSaleMatch {
  invoiceId: string;
  code: string;
  date: string;
  soldAt: string | null;
  sessionCode: string | null;
  customerName: string;
  isWalkIn: boolean;
  grandTotal: number;
  /** Phase 84 -- the selling location's name when it is not this till's (found by its full number); else null. */
  soldAtLocationName: string | null;
  /** Phase 84 -- nothing is left to give back. */
  fullyRefunded: boolean;
}

export interface PosRefundableLine {
  invoiceLineId: string;
  productId: string;
  productName: string;
  unitShortName: string;
  sold: number;
  /** Sold, less what earlier credit notes returned. */
  remaining: number;
  rate: number;
  discountPct: number;
  serviceChargeRate: number;
  /** Phase 70 -- what the sale line was charged for packing. */
  packagingFeeAmount: number;
  vatRate: VatRate;
  lineTotal: number;
  /** Phase 74 -- the sale line's Amount discount, rupees off each unit. */
  discountPerUnit: number;
  /** Phase 91 -- the units the line sold, for a serial-tracked product; empty otherwise. */
  serialNumbers?: string[] | null;
  /** Phase 91 -- those of them no approved refund has brought back: what the refund offers to tick. */
  returnableSerialNumbers?: string[] | null;
}

export interface PosPriorRefund {
  creditNoteId: string;
  code: string;
  date: string;
  grandTotal: number;
}

/** A till sale as the refund screen needs it. */
export interface PosRefundableSale {
  invoiceId: string;
  code: string;
  date: string;
  soldAt: string | null;
  locationId: string | null;
  sessionCode: string | null;
  contactId: string;
  customerName: string;
  isWalkIn: boolean;
  grandTotal: number;
  /** Paid at the till: tendered less change. */
  settled: number;
  /** Still owed on this sale; a refund comes off this first (phase-63-status.md Decision D). */
  owed: number;
  lines: PosRefundableLine[];
  priorRefunds: PosPriorRefund[];
  canRefund: boolean;
  /** Phase 84 -- the selling location's name when it is not this till's; null for a sale of this location. */
  soldAtLocationName: string | null;
  /** Phase 84 -- where the returned goods go back into stock. */
  returnWarehouseName: string | null;
}

export interface PosRefundLineInput {
  invoiceLineId: string;
  quantity: number;
  /** Phase 91 -- which of a serial-tracked line's units came back, one per unit. */
  serialNumbers?: string[];
}

export interface PreviewPosRefundRequest {
  sessionId: string;
  invoiceId: string;
  lines: PosRefundLineInput[];
}

export interface PosRefundPreviewLine {
  invoiceLineId: string;
  quantity: number;
  amount: number;
  serviceChargeAmount: number;
  /** Phase 70. */
  packagingFeeAmount: number;
  vatAmount: number;
  lineTotal: number;
}

/** The server's figure for a refund before it is made -- the refund's own planner, so the screen and the ledger agree. */
export interface PosRefundPreview {
  lines: PosRefundPreviewLine[];
  subTotal: number;
  serviceCharge: number;
  /** Phase 70. */
  packagingFee: number;
  vat: number;
  roundOff: number;
  grandTotal: number;
  owedBefore: number;
  /** What must be handed back; payouts must come to exactly this. */
  requiredPayout: number;
  /** What comes off what the customer owes instead. */
  toAccount: number;
}

/** Every field the endpoint's request record takes (phase 27b). */
export interface CreatePosRefundRequest {
  sessionId: string;
  locationId: string;
  invoiceId: string;
  lines: PosRefundLineInput[];
  payouts: PosTenderInput[];
  reason: string;
}

export interface CreatePosRefundResult {
  id: string;
  code: string;
  grandTotal: number;
  serviceCharge: number;
  /** Phase 70. */
  packagingFee: number;
  roundOff: number;
  paidOut: number;
  toAccount: number;
}

export type CreditNoteStatusValue = 'Draft' | 'Approved' | 'Void';

/** One refund paid out of a session's drawer. */
export interface PosSessionRefund {
  creditNoteId: string;
  code: string;
  status: CreditNoteStatusValue;
  refundedAt: string | null;
  invoiceId: string | null;
  invoiceCode: string | null;
  customerName: string;
  isWalkIn: boolean;
  reason: string | null;
  grandTotal: number;
  paidOut: number;
  toAccount: number;
  printCount: number;
}

/** The heading a receipt prints (phase-62-status.md Decision A). */
export type PosReceiptTitle = 'Invoice' | 'TaxInvoice' | 'AbbreviatedTaxInvoice';

export const POS_RECEIPT_TITLES: Readonly<Record<PosReceiptTitle, string>> = {
  Invoice: 'Invoice',
  TaxInvoice: 'Tax Invoice',
  AbbreviatedTaxInvoice: 'Abbreviated Tax Invoice',
};

export interface PosReceiptLine {
  productName: string;
  quantity: number;
  unitShortName: string;
  rate: number;
  discountPct: number;
  amount: number;
  serviceChargeAmount: number;
  /** Phase 70. */
  packagingFeeAmount: number;
  vatRate: VatRate;
  vatAmount: number;
  lineTotal: number;
  /** Phase 74 -- rupees off each unit; zero unless the line had an Amount discount. */
  discountPerUnit: number;
}

export interface PosReceiptTender {
  paymentModeName: string;
  kind: PaymentModeKind;
  amount: number;
}

/** One printing of a till sale's receipt. `printNumber` 1 is the original; above 1 it is a copy. */
export interface PosReceipt {
  invoiceId: string;
  code: string;
  title: PosReceiptTitle;
  printNumber: number;
  printedAt: string;
  printedByName: string;
  sellerName: string;
  sellerAddress: string | null;
  sellerPan: string | null;
  locationName: string;
  /** Phase 75 -- printed under the location's name; the seller address above is the registered one. */
  locationAddress: string | null;
  date: string;
  soldAt: string | null;
  sessionCode: string;
  cashierName: string;
  orderType: PosTab | null;
  customerName: string;
  customerAddress: string | null;
  customerPan: string | null;
  isWalkIn: boolean;
  lines: PosReceiptLine[];
  grossAmount: number;
  discountAmount: number;
  subTotal: number;
  serviceCharge: number;
  /** Phase 70 -- what the parcels on the bill were charged for packing. */
  packagingFee: number;
  taxableAmount: number;
  nonTaxableAmount: number;
  vat: number;
  roundOff: number;
  grandTotal: number;
  amountInWords: string;
  tenders: PosReceiptTender[];
  tendered: number;
  changeAmount: number;
  creditAmount: number;
  /** Phase 91 -- of `discountAmount`, one guest's own discount on a restaurant bill, printed apart from the order's. */
  partDiscountAmount?: number;
  /** Phase 91 -- the service charge that guest declined; the bill carries none. */
  serviceChargeWaived?: number;
}

/** Phase 63 -- one printing of a till refund's credit note, carrying VAT Rules Rule 20's particulars. */
export interface PosRefundReceipt {
  creditNoteId: string;
  code: string;
  printNumber: number;
  printedAt: string;
  printedByName: string;
  sellerName: string;
  sellerAddress: string | null;
  sellerPan: string | null;
  sellerVatRegistered: boolean;
  locationName: string;
  locationAddress: string | null;
  date: string;
  refundedAt: string | null;
  sessionCode: string;
  cashierName: string;
  invoiceCode: string | null;
  invoiceDate: string | null;
  customerName: string;
  customerAddress: string | null;
  customerPan: string | null;
  isWalkIn: boolean;
  reason: string | null;
  lines: PosReceiptLine[];
  grossAmount: number;
  discountAmount: number;
  subTotal: number;
  serviceCharge: number;
  /** Phase 70 -- what the parcels on the bill were charged for packing. */
  packagingFee: number;
  taxableAmount: number;
  nonTaxableAmount: number;
  vat: number;
  roundOff: number;
  grandTotal: number;
  amountInWords: string;
  payouts: PosReceiptTender[];
  paidOut: number;
  toAccount: number;
}
