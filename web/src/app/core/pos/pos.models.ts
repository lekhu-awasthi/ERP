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
