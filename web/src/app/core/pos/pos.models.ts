import { ProductType, VatRate } from '../catalog/catalog.models';
import { PaymentModeKind } from '../configuration/configuration.models';
import { PosMode } from '../organizations/organizations.models';

// Phase 60 -- the ERP side of point-of-sale configuration (docs/phase-60-status.md). Mirrors
// Application.Pos's DTOs; enums arrive as strings (JsonStringEnumConverter).

/** Which tab a till opens on. Mirrors Domain.Pos.PosTab. */
export type PosTab = 'Retail' | 'DineIn' | 'TakeAway' | 'Delivery';

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
  batchTracking: boolean;
  serialTracking: boolean;
  barcode: string | null;
  units: PosProductUnit[];
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
}

/** Phase 63 -- the refunds half of PosSalesReader's shape. */
export interface PosRefundsSummary {
  refundsCount: number;
  subTotal: number;
  serviceCharge: number;
  vat: number;
  roundOff: number;
  grandTotal: number;
  payouts: PosTenderTotal[];
  paidOut: number;
  toAccount: number;
  cashRefunds: number;
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
  unitId: string | null;
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
  overrideStockWarning: boolean;
  overrideCreditLimitWarning: boolean;
}

export interface CreatePosSaleResult {
  id: string;
  code: string;
  grandTotal: number;
  serviceCharge: number;
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
  vatRate: VatRate;
  lineTotal: number;
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
}

export interface PosRefundLineInput {
  invoiceLineId: string;
  quantity: number;
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
  vatAmount: number;
  lineTotal: number;
}

/** The server's figure for a refund before it is made -- the refund's own planner, so the screen and the ledger agree. */
export interface PosRefundPreview {
  lines: PosRefundPreviewLine[];
  subTotal: number;
  serviceCharge: number;
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
  vatRate: VatRate;
  vatAmount: number;
  lineTotal: number;
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
