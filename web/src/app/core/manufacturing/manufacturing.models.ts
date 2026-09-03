/** Wire shapes for Phase 25's Manufacturing endpoints (FR-8.8/8.9). */

export type ProductionOrderStatus = 'Draft' | 'Approved' | 'Converted' | 'Void';
export type ProductionJournalStatus = 'Draft' | 'Approved' | 'Void';

export interface ProductionRawMaterialLineInput {
  productId: string;
  quantity: number;
}

export interface ProductionByProductLineInput {
  productId: string;
  costAllocationPct: number;
  quantity: number;
}

export interface ProductionExpenseLineInput {
  costTermId: string;
  amount: number;
}

// ---- Bill of Materials ----

export interface BillOfMaterialsListItem {
  id: string;
  productId: string;
  productName: string;
  productCode: string;
  unitName: string | null;
  outputQuantity: number;
  rawMaterialCount: number;
  byProductCount: number;
  manufactureOnEverySale: boolean;
  isActive: boolean;
}

export interface BomRawMaterialLine {
  id: string;
  productId: string;
  productName: string;
  productCode: string;
  unitName: string | null;
  quantity: number;
  quantityPerUnit: number;
}

export interface BomByProductLine extends BomRawMaterialLine {
  costAllocationPct: number;
}

export interface BomExpenseLine {
  id: string;
  costTermId: string;
  costTermName: string;
  amount: number;
  amountPerUnit: number;
}

export interface BillOfMaterialsDetail {
  id: string;
  productId: string;
  productName: string;
  productCode: string;
  unitName: string | null;
  outputQuantity: number;
  manufactureOnEverySale: boolean;
  notes: string | null;
  isActive: boolean;
  createdAt: string;
  rawMaterials: BomRawMaterialLine[];
  byProducts: BomByProductLine[];
  expenses: BomExpenseLine[];
}

export interface BillOfMaterialsRequest {
  productId: string;
  outputQuantity: number;
  manufactureOnEverySale: boolean;
  notes: string | null;
  isActive: boolean;
  rawMaterials: ProductionRawMaterialLineInput[];
  byProducts: ProductionByProductLineInput[];
  expenses: ProductionExpenseLineInput[];
}

/** The server side of "LOAD BOM": lines already scaled to the requested output quantity. */
export interface BomTemplate {
  billOfMaterialsId: string;
  bomOutputQuantity: number;
  outputQuantity: number;
  rawMaterials: { productId: string; productName: string; productCode: string; quantity: number }[];
  byProducts: {
    productId: string;
    productName: string;
    productCode: string;
    costAllocationPct: number;
    quantity: number;
  }[];
  expenses: { costTermId: string; costTermName: string; amount: number }[];
}

// ---- Production Order ----

export interface ProductionOrderListItem {
  id: string;
  code: string;
  date: string;
  reference: string | null;
  productId: string;
  productName: string;
  outputQuantity: number;
  status: ProductionOrderStatus;
  // Phase 27a: the tenant-defined pipeline value the list grid's STATUS column shows, orthogonal to
  // `status` above. The reference product labels this column STATUS on Production Order and STAGE on
  // Sales Order/Quotation, but it is the same control over the same CustomStatus lookup.
  customStatusId: string | null;
}

export interface ProductionOrderRawMaterialLine {
  id: string;
  productId: string;
  productName: string;
  productCode: string;
  unitName: string | null;
  quantity: number;
}

export interface ProductionOrderByProductLine extends ProductionOrderRawMaterialLine {
  costAllocationPct: number;
}

export interface ProductionExpenseLine {
  id: string;
  costTermId: string;
  costTermName: string;
  amount: number;
}

export interface ProductionOrderDetail {
  id: string;
  code: string;
  date: string;
  reference: string | null;
  productId: string;
  productName: string;
  productCode: string;
  unitName: string | null;
  outputQuantity: number;
  billOfMaterialsId: string | null;
  notes: string | null;
  status: ProductionOrderStatus;
  convertedToProductionJournalId: string | null;
  convertedToProductionJournalCode: string | null;
  approvedAt: string | null;
  voidedAt: string | null;
  createdAt: string;
  rawMaterials: ProductionOrderRawMaterialLine[];
  byProducts: ProductionOrderByProductLine[];
  expenses: ProductionExpenseLine[];
}

export interface ProductionOrderRequest {
  date: string;
  reference: string | null;
  productId: string;
  outputQuantity: number;
  billOfMaterialsId: string | null;
  notes: string | null;
  rawMaterials: ProductionRawMaterialLineInput[];
  byProducts: ProductionByProductLineInput[];
  expenses: ProductionExpenseLineInput[];
}

export interface ProductionJournalConversionTemplate {
  date: string;
  reference: string | null;
  productId: string;
  productName: string;
  outputQuantity: number;
  billOfMaterialsId: string | null;
  notes: string | null;
  referrerType: string;
  referrerId: string;
  rawMaterials: ProductionRawMaterialLineInput[];
  byProducts: ProductionByProductLineInput[];
  expenses: ProductionExpenseLineInput[];
}

// ---- Production Journal ----

export interface ProductionJournalListItem {
  id: string;
  code: string;
  date: string;
  reference: string | null;
  productId: string;
  productName: string;
  outputQuantity: number;
  finishedGoodsCost: number | null;
  status: ProductionJournalStatus;
}

export interface ProductionJournalRawMaterialLine {
  id: string;
  productId: string;
  productName: string;
  productCode: string;
  unitName: string | null;
  quantity: number;
  rate: number | null;
  amount: number | null;
}

export interface ProductionJournalByProductLine extends ProductionJournalRawMaterialLine {
  costAllocationPct: number;
}

export interface ProductionGlLine {
  id: string;
  accountId: string;
  debit: number;
  credit: number;
}

export interface ProductionJournalDetail {
  id: string;
  code: string;
  date: string;
  reference: string | null;
  productId: string;
  productName: string;
  productCode: string;
  unitName: string | null;
  outputQuantity: number;
  warehouseId: string;
  billOfMaterialsId: string | null;
  notes: string | null;
  status: ProductionJournalStatus;
  referrerType: string | null;
  referrerId: string | null;
  rawMaterialCost: number | null;
  productionExpenseCost: number | null;
  totalCostOfProduction: number | null;
  costAllocatedToByProduct: number | null;
  finishedGoodsCost: number | null;
  finishedGoodsUnitCost: number | null;
  costRoundingAdjustment: number | null;
  approvedAt: string | null;
  voidedAt: string | null;
  createdAt: string;
  rawMaterials: ProductionJournalRawMaterialLine[];
  byProducts: ProductionJournalByProductLine[];
  expenses: ProductionExpenseLine[];
  glLines: ProductionGlLine[] | null;
}

export interface ProductionJournalRequest {
  date: string;
  reference: string | null;
  productId: string;
  outputQuantity: number;
  warehouseId: string;
  billOfMaterialsId: string | null;
  notes: string | null;
  referrerType: string | null;
  referrerId: string | null;
  rawMaterials: ProductionRawMaterialLineInput[];
  byProducts: ProductionByProductLineInput[];
  expenses: ProductionExpenseLineInput[];
}

export interface CreateDocumentResult {
  id: string;
  code: string;
}

export interface ApproveProductionJournalResult {
  id: string;
  code: string;
  status: ProductionJournalStatus;
  rawMaterialCost: number;
  productionExpenseCost: number;
  totalCostOfProduction: number;
  costAllocatedToByProduct: number;
  finishedGoodsCost: number;
  finishedGoodsUnitCost: number;
  costRoundingAdjustment: number;
}

// ---- Reports ----

export interface ProductionSummaryItem {
  productId: string;
  productName: string;
  productCode: string;
  unitName: string | null;
  quantity: number;
  rate: number | null;
  amount: number | null;
}

export interface ProductionSummaryRow {
  id: string;
  date: string;
  code: string;
  reference: string | null;
  finishedGood: ProductionSummaryItem;
  rawMaterials: ProductionSummaryItem[];
  byProducts: ProductionSummaryItem[];
  expenses: { costTermName: string; amount: number }[];
  rawMaterialCost: number;
  productionExpenseCost: number;
  totalCostOfProduction: number;
  costAllocatedToByProduct: number;
  finishedGoodsCost: number;
}

export interface ProductionSummaryTotals {
  rawMaterialCost: number;
  productionExpenseCost: number;
  costAllocatedToByProduct: number;
  finishedGoodsCost: number;
}

export interface ProductionSummaryReport {
  rows: { items: ProductionSummaryRow[]; page: number; pageSize: number; totalCount: number };
  totals: ProductionSummaryTotals;
}

export interface ProductionVarianceLine {
  productId: string;
  productName: string;
  productCode: string;
  unitName: string | null;
  isByProduct: boolean;
  voucherQuantity: number;
  bomQuantity: number;
  varianceQuantity: number;
  variancePct: number | null;
}

export interface ProductionVarianceRow {
  id: string;
  date: string;
  code: string;
  reference: string | null;
  productId: string;
  productName: string;
  quantityProduced: number;
  lines: ProductionVarianceLine[];
}

export interface ProductionPlanningLine {
  productId: string;
  productName: string;
  productCode: string;
  unitName: string | null;
  quantityRequired: number;
  quantityAvailable: number;
  surplus: number;
}

export interface ProductionPlanningReport {
  productId: string;
  productName: string;
  quantity: number;
  billOfMaterialsId: string | null;
  bomOutputQuantity: number | null;
  multipleLevel: boolean;
  lines: ProductionPlanningLine[];
}
