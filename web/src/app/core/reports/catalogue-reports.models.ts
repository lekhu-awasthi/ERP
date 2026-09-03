/**
 * Phase 26c -- the Inventory, Tax, System and Analytics report groups that complete the catalogue.
 * Every shape here mirrors a DTO on the server one-for-one; nothing is derived client-side, and in
 * particular no footer total is summed in the browser (phase-16c bug #1).
 */

export type InventoryBalanceFilter = 'All' | 'PositiveOnly' | 'NegativeOnly';

export interface InventoryPositionRowDto {
  readonly productId: string;
  readonly product: string;
  readonly category: string;
  readonly quantity: number;
  readonly unit: string;
  readonly rate: number;
  readonly amount: number;
}

export interface InventoryPositionReportDto {
  readonly fromDate: string;
  readonly toDate: string;
  readonly items: InventoryPositionRowDto[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalQuantity: number;
  readonly totalAmount: number;
}

/** One of Inventory Movement's four column groups. */
export interface InventoryMovementMeasureDto {
  readonly quantity: number;
  readonly rate: number;
  readonly value: number;
}

export interface InventoryMovementRowDto {
  readonly productId: string;
  readonly product: string;
  readonly category: string;
  readonly opening: InventoryMovementMeasureDto;
  readonly in: InventoryMovementMeasureDto;
  readonly out: InventoryMovementMeasureDto;
  readonly balance: InventoryMovementMeasureDto;
}

export interface InventoryMovementReportDto {
  readonly fromDate: string;
  readonly toDate: string;
  readonly items: InventoryMovementRowDto[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalOpeningValue: number;
  readonly totalInValue: number;
  readonly totalOutValue: number;
  readonly totalBalanceValue: number;
}

export interface InventoryLedgerReportRowDto {
  readonly id: string;
  readonly date: string;
  readonly documentType: string;
  readonly sourceDocumentId: string;
  readonly documentCode: string;
  readonly reference: string | null;
  readonly contact: string | null;
  readonly warehouse: string;
  readonly direction: string;
  readonly inQuantity: number;
  readonly inRate: number;
  readonly inValue: number;
  readonly outQuantity: number;
  readonly outRate: number;
  readonly outValue: number;
  readonly balanceQuantity: number;
  readonly balanceRate: number;
  readonly balanceValue: number;
}

/**
 * The Opening and Closing bracket rows are their own fields rather than rows in `items`, because
 * they must survive pagination -- the pager counts only the movement rows.
 */
export interface InventoryLedgerReportDto {
  readonly fromDate: string;
  readonly toDate: string;
  readonly productId: string;
  readonly product: string;
  readonly openingQuantity: number;
  readonly openingRate: number;
  readonly openingValue: number;
  readonly closingQuantity: number;
  readonly closingRate: number;
  readonly closingValue: number;
  readonly items: InventoryLedgerReportRowDto[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

export interface InventoryMasterRowDto {
  readonly entryDate: string;
  readonly contact: string | null;
  readonly documentType: string;
  readonly sourceDocumentId: string;
  readonly warehouse: string | null;
  readonly account: string | null;
  readonly entryNo: string;
  readonly reference: string | null;
  readonly productId: string;
  readonly product: string;
  readonly category: string;
  readonly quantity: number;
  readonly unit: string;
  readonly rate: number;
  readonly amount: number;
  readonly itemDiscount: number;
  readonly transactionDiscount: number;
  readonly netAmount: number;
  readonly vatAmount: number;
  readonly totalAmount: number;
  readonly additionalCost: number;
}

export interface InventoryMasterReportDto {
  readonly fromDate: string;
  readonly toDate: string;
  readonly items: InventoryMasterRowDto[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalNetAmount: number;
  readonly totalVatAmount: number;
  readonly totalAmount: number;
}

export interface SalesReturnRegisterRowDto {
  readonly date: string;
  readonly documentCode: string;
  readonly contactId: string;
  readonly contactName: string;
  readonly contactPan: string | null;
  readonly totalReturnValue: number;
  readonly taxExemptReturnValue: number;
  readonly taxableReturnValue: number;
  readonly vatAmount: number;
}

export interface SalesReturnRegisterDto {
  readonly fromDate: string;
  readonly toDate: string;
  readonly items: SalesReturnRegisterRowDto[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalReturnValue: number;
  readonly totalTaxExemptReturnValue: number;
  readonly totalTaxableReturnValue: number;
  readonly totalVatAmount: number;
}

/** Seven money columns, not four: this register mirrors the Purchase Register, not the sales one. */
export interface PurchaseReturnRegisterRowDto {
  readonly date: string;
  readonly documentCode: string;
  readonly importDeclarationNo: string | null;
  readonly contactId: string;
  readonly contactName: string;
  readonly contactPan: string | null;
  readonly totalReturnValue: number;
  readonly taxExemptValue: number;
  readonly taxableNonCapitalLocalValue: number;
  readonly taxableNonCapitalLocalVat: number;
  readonly taxableNonCapitalImportValue: number;
  readonly taxableNonCapitalImportVat: number;
  readonly taxableCapitalValue: number;
  readonly taxableCapitalVat: number;
}

export interface PurchaseReturnRegisterDto {
  readonly fromDate: string;
  readonly toDate: string;
  readonly items: PurchaseReturnRegisterRowDto[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalReturnValue: number;
  readonly totalTaxExemptValue: number;
  readonly totalTaxableNonCapitalLocalValue: number;
  readonly totalTaxableNonCapitalLocalVat: number;
  readonly totalTaxableNonCapitalImportValue: number;
  readonly totalTaxableNonCapitalImportVat: number;
  readonly totalTaxableCapitalValue: number;
  readonly totalTaxableCapitalVat: number;
}

export interface NetTradingAssetsRowDto {
  readonly particulars: string;
  readonly balance: number;
  readonly compareBalance: number | null;
  readonly children: NetTradingAssetsRowDto[];
}

export interface NetTradingAssetsDto {
  readonly fromDate: string;
  readonly toDate: string;
  readonly excludeAdvance: boolean;
  /** The date the Compare column was computed at, so the header can name it. */
  readonly compareAsOfDate: string | null;
  readonly rows: NetTradingAssetsRowDto[];
}

/**
 * `balanceType` is null on the two inventory rows -- a stock valuation does not sit on a side of
 * the ledger, and the live report leaves those cells blank. `isModelled` is false for the one row
 * this codebase has no concept behind.
 */
export interface ExceptionalReportRowDto {
  readonly particulars: string;
  readonly balance: number;
  readonly balanceType: string | null;
  readonly isModelled: boolean;
}

export interface ExceptionalReportDto {
  readonly fromDate: string;
  readonly toDate: string;
  readonly rows: ExceptionalReportRowDto[];
}

export interface UserLogRowDto {
  readonly id: string;
  readonly userId: string | null;
  readonly fullName: string;
  readonly email: string;
  readonly occurredAt: string;
  readonly deviceOs: string | null;
  readonly ipAddress: string | null;
  readonly outcome: string;
  readonly description: string;
  readonly browser: string | null;
}

export interface UserLogDto {
  readonly fromDate: string;
  readonly toDate: string;
  readonly items: UserLogRowDto[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}
