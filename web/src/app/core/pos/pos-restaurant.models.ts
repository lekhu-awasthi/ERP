import { VatRate } from '../catalog/catalog.models';
import { PosTab, PosTillCategory, PosWalkInCustomer } from './pos.models';

// Phase 64 -- the restaurant: floor plan, kitchen stations, orders and kitchen tickets. Mirrors
// Application.Pos's restaurant DTOs; enums arrive as strings.

export type PosTableShape = 'Rectangle' | 'Circle';
/** Phase 65 adds Settled: everything billed, the table free; the kitchen may still be cooking it. */
export type PosOrderStatus = 'Open' | 'Voided' | 'Settled';

/** The three tabs an order can be: a Retail hold never is one (it stays in the browser). */
export type PosOrderType = Extract<PosTab, 'DineIn' | 'TakeAway' | 'Delivery'>;

export const POS_ORDER_TYPES: readonly PosOrderType[] = ['DineIn', 'TakeAway', 'Delivery'];

// ---- Floor plan (Configurations, Pos.FloorPlan.Manage) ------------------------------------------

export interface PosFloorTable {
  id: string;
  name: string;
  capacity: number;
  shape: PosTableShape;
  x: number;
  y: number;
  width: number;
  height: number;
  isActive: boolean;
  /** An open order is seated here, so it cannot be made inactive. */
  isOccupied: boolean;
}

export interface PosFloorArea {
  id: string;
  name: string;
  isActive: boolean;
  tables: PosFloorTable[];
}

export interface PosFloorPlan {
  locationId: string;
  locationName: string;
  canvasWidth: number;
  canvasHeight: number;
  maxCapacity: number;
  areas: PosFloorArea[];
}

export interface PosTableLayoutInput {
  id: string | null;
  name: string;
  capacity: number;
  shape: PosTableShape;
  x: number;
  y: number;
  width: number;
  height: number;
  isActive: boolean;
}

// ---- Kitchen stations (Configurations, Pos.Settings.Manage) -------------------------------------

export interface KitchenStationProduct {
  id: string;
  code: string;
  name: string;
}

export interface KitchenStation {
  id: string;
  name: string;
  isActive: boolean;
  products: KitchenStationProduct[];
}

/** What a ticket with no station is called, on screen and on paper. */
export const DEFAULT_KITCHEN_STATION = 'Default';

// ---- The restaurant till (Pos.Order.Operate) ----------------------------------------------------

export interface PosOpenOrder {
  id: string;
  code: string;
  orderType: PosOrderType;
  tableId: string | null;
  covers: number;
  contactName: string;
  createdAt: string;
  /** Items still to come from the kitchen; zero means everything was served. */
  outstanding: number;
  total: number;
  /** Phase 65 -- what its bills (not voided) came to; non-zero on a part-paid tab. */
  billed: number;
  /** Phase 65 -- items not yet on a bill. */
  toBill: number;
}

export interface PosRestaurantTable {
  id: string;
  name: string;
  capacity: number;
  shape: PosTableShape;
  x: number;
  y: number;
  width: number;
  height: number;
  order: PosOpenOrder | null;
}

export interface PosRestaurantArea {
  id: string;
  name: string;
  tables: PosRestaurantTable[];
}

export interface PosRestaurant {
  locationId: string;
  locationCode: string;
  locationName: string;
  availableTabs: PosTab[];
  defaultTab: PosTab | null;
  serviceChargeEnabled: boolean;
  serviceChargeRate: number;
  printKot: boolean;
  canvasWidth: number;
  canvasHeight: number;
  walkInCustomer: PosWalkInCustomer | null;
  categories: PosTillCategory[];
  areas: PosRestaurantArea[];
  /** Every open order here; the Dine In ones are also on their tables. */
  orders: PosOpenOrder[];
  /** Whether this user holds Pos.Order.Void, which discarding needs. */
  canVoid: boolean;
  /** Phase 65 -- billing reads these. */
  roundOffEnabled: boolean;
  printEstimateBill: boolean;
  printInvoice: boolean;
  /** Phase 65 -- this user's open drawer here, which taking payment needs; null for a waiter. */
  mySessionId: string | null;
  mySessionCode: string | null;
  /** Phase 65 -- whether this user holds Pos.Kitchen.Operate, so the floor can link to the board. */
  canKitchen: boolean;
}

export interface PosOrderLine {
  id: string;
  lineNo: number;
  productId: string;
  productCode: string;
  productName: string;
  unitId: string | null;
  unitName: string | null;
  rate: number;
  vatRate: VatRate;
  serviceChargeRate: number;
  note: string | null;
  kitchenStationId: string | null;
  kitchenStationName: string;
  ordered: number;
  discarded: number;
  /** The net quantity the guest pays for. */
  quantity: number;
  served: number;
  outstanding: number;
  /** Phase 65 -- on bills not voided; a sum over the invoice lines naming this line. */
  invoiced: number;
  /** Phase 65 -- the quantity not yet on a bill. */
  toBill: number;
  amount: number;
  serviceChargeAmount: number;
  vatAmount: number;
  total: number;
}

export interface KitchenTicketLine {
  orderLineId: string;
  lineNo: number;
  productName: string;
  unitName: string | null;
  /** Positive on a send, negative on a cancellation. */
  quantity: number;
  note: string | null;
}

export interface KitchenTicket {
  id: string;
  sendNumber: number;
  /** What the paper prints: the order's code and the send, ORD0007-2. */
  number: string;
  kitchenStationId: string | null;
  kitchenStationName: string;
  isCancellation: boolean;
  reason: string | null;
  createdAt: string;
  createdByName: string;
  printCount: number;
  lines: KitchenTicketLine[];
}

export interface PosOrder {
  id: string;
  code: string;
  locationId: string;
  locationCode: string;
  locationName: string;
  orderType: PosOrderType;
  status: PosOrderStatus;
  tableId: string | null;
  tableName: string | null;
  areaId: string | null;
  areaName: string | null;
  covers: number;
  contactId: string | null;
  contactName: string;
  date: string;
  createdAt: string;
  createdByName: string;
  voidReason: string | null;
  voidedAt: string | null;
  voidedByName: string | null;
  amount: number;
  serviceCharge: number;
  vat: number;
  /** The estimate before any bill, not yet rounded to the rupee. */
  total: number;
  outstanding: number;
  /** Phase 65 -- when the last of it was billed. */
  settledAt: string | null;
  /** Phase 65 -- what its bills not voided came to. */
  billed: number;
  /** Phase 65 -- items not yet on a bill. */
  toBill: number;
  lines: PosOrderLine[];
  tickets: KitchenTicket[];
  invoices: PosOrderInvoice[];
}

/** Phase 65 -- one bill an order has had; voided ones stay listed. */
export interface PosOrderInvoice {
  id: string;
  code: string;
  grandTotal: number;
  serviceCharge: number;
  roundOff: number;
  isVoided: boolean;
}

export interface PosOrderItemInput {
  productId: string;
  quantity: number;
  unitId: string | null;
  note: string | null;
}

export interface PosOrderLineQuantityInput {
  lineId: string;
  quantity: number;
}

export interface CreatePosOrderRequest {
  locationId: string;
  orderType: PosOrderType;
  tableId: string | null;
  covers: number;
  contactId: string | null;
  items: PosOrderItemInput[];
}

export interface UpdatePosOrderRequest {
  tableId: string | null;
  covers: number;
  contactId: string | null;
}

export interface PosKitchenTicketPrint {
  order: PosOrder;
  ticketId: string;
  /** 1 is the original; anything later prints as a reprint. */
  printNumber: number;
}

// ---- Phase 65: billing an order ---------------------------------------------------------------------

/** Whole: everything left. Items: chosen lines in chosen quantities. Equal: one of N equal parts. */
export type PosOrderSplit = 'Whole' | 'Items' | 'Equal';

export interface PosOrderBillRequest {
  split: PosOrderSplit;
  items: PosOrderLineQuantityInput[];
  /** For an equal split: how many parts what is left is split into, this one included. */
  parts: number | null;
}

export interface PosOrderBillLine {
  orderLineId: string;
  lineNo: number;
  productName: string;
  unitName: string | null;
  note: string | null;
  quantity: number;
  rate: number;
  serviceChargeRate: number;
  amount: number;
  serviceChargeAmount: number;
  vatAmount: number;
  total: number;
  remainingAfter: number;
}

/** A part priced by the server: what the screen shows before payment and what the estimate prints. */
export interface PosOrderBillPreview {
  orderId: string;
  orderCode: string;
  lines: PosOrderBillLine[];
  amount: number;
  serviceCharge: number;
  vat: number;
  unrounded: number;
  roundOff: number;
  /** What this bill comes to. */
  total: number;
  billsTheRest: boolean;
  /** What the whole order comes to as bills. */
  orderTotal: number;
  billedBefore: number;
  /** What later parts will pay. */
  leftAfter: number;
}

export interface CreatePosOrderInvoiceRequest extends PosOrderBillRequest {
  sessionId: string;
  locationId: string;
  tenders: { paymentModeId: string; amount: number }[];
  changeAmount: number;
  contactId: string | null;
  overrideStockWarning: boolean;
  overrideCreditLimitWarning: boolean;
}

export interface CreatePosOrderInvoiceResult {
  id: string;
  code: string;
  grandTotal: number;
  serviceCharge: number;
  roundOff: number;
  tendered: number;
  changeAmount: number;
  creditAmount: number;
  isAbbreviatedTaxInvoice: boolean;
  orderStatus: PosOrderStatus;
  /** What of the order is still to be billed, in money. */
  orderToBill: number;
}

// ---- Phase 65: the kitchen board (Pos.Kitchen.Operate) ----------------------------------------------

export type PosKitchenBoardView = 'Pending' | 'Served' | 'All';
export type KitchenTicketState = 'Pending' | 'Served' | 'Cancelled' | 'Cancellation';

export interface PosKitchenBoardLine {
  orderLineId: string;
  productName: string;
  unitName: string | null;
  note: string | null;
  sent: number;
  served: number;
  cancelled: number;
  pending: number;
}

export interface PosKitchenBoardTicket {
  id: string;
  number: string;
  orderId: string;
  orderCode: string;
  orderType: PosOrderType;
  orderStatus: PosOrderStatus;
  /** The table, or the customer of a Take Away or Delivery. */
  label: string;
  areaName: string | null;
  covers: number;
  kitchenStationId: string | null;
  kitchenStationName: string;
  state: KitchenTicketState;
  reason: string | null;
  createdAt: string;
  createdByName: string;
  lines: PosKitchenBoardLine[];
}

export interface PosKitchenSummary {
  productName: string;
  unitName: string | null;
  pending: number;
}

export interface PosKitchenBoard {
  locationId: string;
  locationName: string;
  /** Default (id null) first, then the active stations. */
  stations: { id: string | null; name: string }[];
  pendingCount: number;
  tickets: PosKitchenBoardTicket[];
  summary: PosKitchenSummary[];
  /** Moves whenever anything on the board could have changed. */
  version: string | null;
  readAt: string;
}

export interface PosKitchenServeResult {
  orderId: string;
  ticketId: string;
  state: KitchenTicketState;
  pending: number;
}
