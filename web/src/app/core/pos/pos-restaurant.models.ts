import { VatRate } from '../catalog/catalog.models';
import { PosTab, PosTillCategory, PosWalkInCustomer } from './pos.models';

// Phase 64 -- the restaurant: floor plan, kitchen stations, orders and kitchen tickets. Mirrors
// Application.Pos's restaurant DTOs; enums arrive as strings.

export type PosTableShape = 'Rectangle' | 'Circle';
export type PosOrderStatus = 'Open' | 'Voided';

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
  lines: PosOrderLine[];
  tickets: KitchenTicket[];
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
