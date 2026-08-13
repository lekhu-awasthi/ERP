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
