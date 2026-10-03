import { PaymentModeKind } from '../configuration/configuration.models';
import { PosSalesSummary, PosSessionStatus, PosTab } from './pos.models';
import { PosOrderInvoice, PosOrderStatus } from './pos-restaurant.models';

/**
 * Phase 66 -- the POS reports and the dashboard. Every money figure is read on the server from
 * PosSalesReader (the till's takings) or TradeLineReader (a product's), the readers the X/Z report and
 * Sales by Item already read, so no screen here adds anything up of its own.
 */

/** One payment mode's takings over a period: what the sales took in it and what refunds paid back. */
export interface PosPaymentModeLine {
  paymentModeId: string;
  paymentModeName: string;
  kind: PaymentModeKind;
  received: number;
  paidBack: number;
  net: number;
}

/** Where net sales went: each mode's net, less change, plus what stayed on accounts = net sales. */
export interface PosPaymentBreakdown {
  modes: PosPaymentModeLine[];
  change: number;
  credit: number;
  total: number;
}

export type PosSalesBucket = 'Hour' | 'Day';

/** One point of a series: a Nepal hour of one day, or one day. */
export interface PosSalesPoint {
  date: string;
  hour: number | null;
  salesCount: number;
  sales: number;
  refunds: number;
  net: number;
}

export interface PosDayReportSession {
  id: string;
  code: string;
  locationId: string;
  locationName: string;
  userId: string;
  userName: string;
  status: PosSessionStatus;
  openedAt: string;
  closedAt: string | null;
  openingFloat: number;
  cashIn: number;
  cashOut: number;
  countedCash: number | null;
  cashDifference: number | null;
}

export interface PosDayReport {
  fromDate: string;
  toDate: string;
  locationId: string | null;
  sales: PosSalesSummary;
  payments: PosPaymentBreakdown;
  bucket: PosSalesBucket;
  series: PosSalesPoint[];
  voidedSales: number;
  voidedRefunds: number;
  voidedOrders: number;
  sessions: PosDayReportSession[];
}

export interface PosDashboardProduct {
  productId: string;
  code: string;
  name: string;
  unit: string | null;
  quantity: number;
  total: number;
}

export interface PosDashboardSession {
  id: string;
  code: string;
  locationId: string;
  locationName: string;
  userName: string;
  openedAt: string;
}

export interface PosDashboard {
  fromDate: string;
  toDate: string;
  locationId: string | null;
  sales: PosSalesSummary;
  payments: PosPaymentBreakdown;
  bucket: PosSalesBucket;
  series: PosSalesPoint[];
  topProducts: PosDashboardProduct[];
  otherProducts: number;
  openOrders: number;
  openOrdersToBill: number;
  openSessions: PosDashboardSession[];
}

export interface PosSessionRow {
  id: string;
  code: string;
  locationId: string;
  locationName: string;
  userId: string;
  userName: string;
  status: PosSessionStatus;
  openedAt: string;
  closedAt: string | null;
  openingFloat: number;
  salesCount: number;
  sales: number;
  refunds: number;
  expectedCash: number | null;
  countedCash: number | null;
  cashDifference: number | null;
}

export interface PosOrderReportRow {
  id: string;
  code: string;
  date: string;
  createdAt: string;
  status: PosOrderStatus;
  orderType: PosTab;
  locationId: string;
  locationName: string;
  areaName: string | null;
  tableName: string | null;
  covers: number;
  contactName: string;
  createdByName: string;
  orderValue: number;
  billed: number;
  billedRoundOff: number;
  toBill: number;
  invoices: PosOrderInvoice[];
  voidReason: string | null;
  settledAt: string | null;
}

export interface PosOrderReport {
  fromDate: string;
  toDate: string;
  items: PosOrderReportRow[];
  page: number;
  pageSize: number;
  totalCount: number;
  openCount: number;
  settledCount: number;
  voidedCount: number;
  billed: number;
  toBill: number;
}

export type PosPaymentType = 'Cash' | 'Card' | 'EPayment' | 'Other' | 'Credit';

export const POS_PAYMENT_TYPES: readonly PosPaymentType[] = ['Cash', 'Card', 'EPayment', 'Other', 'Credit'];

export const POS_PAYMENT_TYPE_LABELS: Readonly<Record<PosPaymentType, string>> = {
  Cash: 'Cash',
  Card: 'Card',
  EPayment: 'E-Payment',
  Other: 'Other',
  Credit: 'Credit (on account)',
};

export type PosPaymentEntry = 'Tender' | 'Change' | 'Credit' | 'Payout' | 'CreditReturned';

export const POS_PAYMENT_ENTRY_LABELS: Readonly<Record<PosPaymentEntry, string>> = {
  Tender: 'Paid',
  Change: 'Change given',
  Credit: 'Left on account',
  Payout: 'Paid back',
  CreditReturned: 'Taken off account',
};

export interface PosPaymentRow {
  date: string;
  at: string;
  documentType: 'Invoice' | 'CreditNote';
  documentId: string;
  code: string;
  locationId: string | null;
  locationName: string | null;
  cashier: string | null;
  contactId: string;
  contactName: string;
  entry: PosPaymentEntry;
  type: PosPaymentType;
  paymentModeId: string | null;
  paymentModeName: string | null;
  accountName: string | null;
  amount: number;
}

export interface PosPaymentSummary {
  fromDate: string;
  toDate: string;
  items: PosPaymentRow[];
  page: number;
  pageSize: number;
  totalCount: number;
  totals: { type: PosPaymentType; amount: number }[];
  total: number;
}
