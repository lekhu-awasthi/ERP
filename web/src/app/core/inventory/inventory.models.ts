import { DocumentType } from '../sales/sales.models';

export type WarehouseTransferStatus = 'Draft' | 'Approved' | 'Void';
export type InventoryAdjustmentStatus = 'Draft' | 'Approved' | 'Void';
export type InventoryAdjustmentDirection = 'Increase' | 'Decrease';
export type StockMovementDirection = 'In' | 'Out';

export interface PostedGlLineDto {
  id: string;
  accountId: string;
  debit: number;
  credit: number;
}

// --- Warehouse Transfer ---

export interface WarehouseTransferLineInput {
  productId: string;
  quantity: number;
}

export interface WarehouseTransfer {
  id: string;
  organizationId: string;
  code: string;
  date: string;
  reference: string | null;
  fromWarehouseId: string;
  toWarehouseId: string;
  status: WarehouseTransferStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
}

export interface WarehouseTransferLineDto extends WarehouseTransferLineInput {
  id: string;
}

export interface WarehouseTransferDetail extends WarehouseTransfer {
  lines: WarehouseTransferLineDto[];
}

export interface WarehouseTransferRequest {
  fromWarehouseId: string;
  toWarehouseId: string;
  date: string;
  reference: string | null;
  lines: WarehouseTransferLineInput[];
}

export interface CreateWarehouseTransferResult {
  id: string;
  code: string;
  status: WarehouseTransferStatus;
}

export interface UpdateWarehouseTransferResult {
  id: string;
  code: string;
  status: WarehouseTransferStatus;
}

export interface ApproveWarehouseTransferResult {
  id: string;
  code: string;
  status: WarehouseTransferStatus;
  approvedAt: string | null;
}

export interface VoidWarehouseTransferResult {
  id: string;
  code: string;
  status: WarehouseTransferStatus;
  voidedAt: string | null;
}

// --- Inventory Adjustment ---

export interface InventoryAdjustmentLineInput {
  productId: string;
  direction: InventoryAdjustmentDirection;
  quantity: number;
  unitCost: number;
}

export interface InventoryAdjustment {
  id: string;
  organizationId: string;
  code: string;
  date: string;
  reference: string | null;
  warehouseId: string;
  status: InventoryAdjustmentStatus;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
}

export interface InventoryAdjustmentLineDto extends InventoryAdjustmentLineInput {
  id: string;
}

export interface InventoryAdjustmentDetail extends InventoryAdjustment {
  lines: InventoryAdjustmentLineDto[];
  glLines: PostedGlLineDto[] | null;
}

export interface InventoryAdjustmentRequest {
  warehouseId: string;
  date: string;
  reference: string | null;
  lines: InventoryAdjustmentLineInput[];
}

export interface CreateInventoryAdjustmentResult {
  id: string;
  code: string;
  status: InventoryAdjustmentStatus;
}

export interface UpdateInventoryAdjustmentResult {
  id: string;
  code: string;
  status: InventoryAdjustmentStatus;
}

export interface ApproveInventoryAdjustmentResult {
  id: string;
  code: string;
  status: InventoryAdjustmentStatus;
  approvedAt: string | null;
}

export interface VoidInventoryAdjustmentResult {
  id: string;
  code: string;
  status: InventoryAdjustmentStatus;
  voidedAt: string | null;
}

// --- Reports ---

export interface StockPositionDto {
  productId: string;
  warehouseId: string;
  opening: number;
  in: number;
  out: number;
  balance: number;
}

export interface InventoryLedgerRowDto {
  id: string;
  transactionDate: string;
  sourceDocumentType: DocumentType;
  sourceDocumentId: string;
  direction: StockMovementDirection;
  quantity: number;
  unitCost: number;
  runningBalance: number;
}

// --- Phase 17: Opening Balances (Product tab) ---

export interface ProductOpeningBalanceDto {
  productId: string;
  productCode: string;
  productName: string;
  categoryName: string;
  quantity: number;
  rate: number;
  amount: number;
}

export interface OpeningStockLineRequest {
  warehouseId: string;
  quantity: number;
  rate: number;
}

export interface OpeningStockLineResult {
  id: string;
  productId: string;
  warehouseId: string;
  quantity: number;
  rate: number;
}

// --- Stock Ageing (Phase 19) ---

export interface StockAgeingRowDto {
  productId: string;
  productCode: string;
  productName: string;
  categoryName: string;
  unitShortName: string;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  days91Plus: number;
  total: number;
  rate: number;
  amount: number;
}

export interface StockAgeingDto {
  asOfDate: string;
  items: StockAgeingRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalDays1To30: number;
  totalDays31To60: number;
  totalDays61To90: number;
  totalDays91Plus: number;
  totalAmount: number;
}

// --- Product Profitability Report (Phase 19) ---

export interface ProductProfitabilityRowDto {
  productId: string;
  productCode: string;
  productName: string;
  categoryName: string;
  openingBalance: number;
  purchase: number;
  productionCost: number;
  additionalCost: number;
  closingBalance: number;
  costOfSales: number;
  sales: number;
  consumption: number;
  grossProfit: number;
  grossMarginPct: number;
}

export interface ProductProfitabilityDto {
  fromDate: string;
  toDate: string;
  items: ProductProfitabilityRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalSales: number;
  totalCostOfSales: number;
  totalGrossProfit: number;
}
