/**
 * Phase 26b's Receivable/Payable and analytics report DTOs.
 *
 * The four By-Customer/By-Supplier and By-Item report pairs are each answered by one backend
 * handler discriminated by a side the route hardcodes, so a single interface serves both screens of
 * a pair. Where the live reports differ only in a column *heading* ("Net Sales" against "Net
 * Purchase"), the difference lives in the template, not here.
 */

export type TradeSide = 'Sales' | 'Purchase';

export type BalanceMarker = 'DR' | 'CR';

// ---- Customer Receivable Summary / Supplier Payable Summary --------------------------------

export interface ContactBalanceSummaryRowDto {
  contactId: string;
  contactCode: string;
  contactName: string;
  contactGroupName: string | null;
  closingBalance: number;
  balanceType: BalanceMarker;
}

export interface ContactBalanceSummaryDto {
  contactType: 'Customer' | 'Supplier';
  fromDate: string;
  toDate: string;
  rows: ContactBalanceSummaryRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalClosingBalance: number;
  totalBalanceType: BalanceMarker;
}

// ---- Invoice Age / Purchase Bill Age -------------------------------------------------------

export type AgeableDocumentType =
  | 'OpeningBalance'
  | 'Invoice'
  | 'PurchaseBill'
  | 'Expense'
  | 'JournalVoucher';

/** The Txn Type filter's options, in the order the live filter lists them per side. */
export const CUSTOMER_AGEABLE_TYPES: readonly AgeableDocumentType[] = [
  'OpeningBalance',
  'Invoice',
  'JournalVoucher',
];

export const SUPPLIER_AGEABLE_TYPES: readonly AgeableDocumentType[] = [
  'OpeningBalance',
  'PurchaseBill',
  'Expense',
  'JournalVoucher',
];

export const AGEABLE_TYPE_LABELS: Readonly<Record<AgeableDocumentType, string>> = {
  OpeningBalance: 'Opening Balance',
  Invoice: 'Invoice',
  PurchaseBill: 'Purchase Bill',
  Expense: 'Expense',
  JournalVoucher: 'Journal Voucher',
};

export interface DocumentAgeRowDto {
  documentType: AgeableDocumentType;
  documentId: string;
  date: string;
  dueDate: string;
  number: string;
  referenceNo: string | null;
  contactId: string;
  contactCode: string;
  contactName: string;
  contactGroupName: string | null;
  amount: number;
  paid: number;
  balance: number;
  status: 'Overdue' | 'Current';
  ageDays: number;
}

export interface DocumentAgeDto {
  contactType: 'Customer' | 'Supplier';
  fromDate: string;
  asOfDate: string;
  rows: DocumentAgeRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalAmount: number;
  totalPaid: number;
  totalBalance: number;
}

// ---- Sales/Purchase By Customer/Supplier ----------------------------------------------------

export interface TradeByContactRowDto {
  contactId: string;
  contactCode: string;
  contactName: string;
  contactGroupName: string | null;
  amount: number;
  discount: number;
  netAmount: number;
  vatAmount: number;
  totalAmount: number;
}

export interface TradeByContactDto {
  side: TradeSide;
  fromDate: string;
  toDate: string;
  rows: TradeByContactRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalAmount: number;
  totalDiscount: number;
  totalNetAmount: number;
  totalVatAmount: number;
  totalTotalAmount: number;
}

// ---- Sales/Purchase By Item ------------------------------------------------------------------

export type TradeItemGrouping = 'Item' | 'Category';

export interface TradeByItemRowDto {
  id: string;
  code: string | null;
  name: string;
  quantity: number;
  amount: number;
  discount: number;
  netAmount: number;
  vatAmount: number;
  totalAmount: number;
}

/** There is no total quantity: the rows are products in different units of measure. */
export interface TradeByItemDto {
  side: TradeSide;
  groupBy: TradeItemGrouping;
  fromDate: string;
  toDate: string;
  rows: TradeByItemRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalAmount: number;
  totalDiscount: number;
  totalNetAmount: number;
  totalVatAmount: number;
  totalTotalAmount: number;
}

// ---- The four BS fiscal-year Monthly crosstabs ----------------------------------------------

export interface TradeMonthlyColumnDto {
  bsYear: number;
  bsMonth: number;
  monthName: string;
  fromDate: string;
  toDate: string;
  label: string;
}

export interface TradeByContactMonthlyRowDto {
  contactId: string;
  contactCode: string;
  contactName: string;
  pan: string | null;
  contactGroupName: string | null;
  monthly: number[];
  quarters: number[];
  total: number;
}

export interface TradeByContactMonthlyDto {
  side: TradeSide;
  fiscalYear: number;
  fromDate: string;
  toDate: string;
  columns: TradeMonthlyColumnDto[];
  rows: TradeByContactMonthlyRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalMonthly: number[];
  totalQuarters: number[];
  total: number;
}

export interface TradeByItemMonthlyRowDto {
  productId: string;
  productCode: string;
  productName: string;
  monthly: number[];
  quarters: number[];
  total: number;
}

export interface TradeByItemMonthlyDto {
  side: TradeSide;
  fiscalYear: number;
  fromDate: string;
  toDate: string;
  columns: TradeMonthlyColumnDto[];
  rows: TradeByItemMonthlyRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalMonthly: number[];
  totalQuarters: number[];
  total: number;
}

export const QUARTER_LABELS: readonly string[] = ['1st Quarter', '2nd Quarter', '3rd Quarter', '4th Quarter'];

/** Months per quarter -- the crosstab inserts a subtotal column after every third month. */
export const MONTHS_PER_QUARTER = 3;

// ---- Sales Summary Report ---------------------------------------------------------------------

export type SalesSummaryMode = 'Date' | 'Month';

/**
 * `label` is set in Month mode ("Shrawan, 2083") and `date` in Date mode, so the client renders the
 * day through the user's own AD/BS preference rather than the server picking a calendar
 * (phase-23's rule).
 */
export interface SalesSummaryRowDto {
  date: string | null;
  label: string | null;
  subTotal: number;
  discount: number;
  nonTaxableSales: number;
  taxableSales: number;
  vat: number;
  total: number;
}

export interface SalesSummaryReportDto {
  fiscalYear: number;
  mode: SalesSummaryMode;
  fromDate: string;
  toDate: string;
  rows: SalesSummaryRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}
