export type ProductType = 'Goods' | 'Service';
export type VatRate = 'NoVat' | 'ZeroVat' | 'ThirteenPercentVat';

export interface ProductCategory {
  id: string;
  organizationId: string;
  name: string;
  parentCategoryId: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface CreateProductCategoryRequest {
  name: string;
  parentCategoryId: string | null;
}

export interface CreateProductCategoryResult {
  id: string;
  name: string;
  parentCategoryId: string | null;
}

export interface UpdateProductCategoryRequest {
  name: string;
  parentCategoryId: string | null;
  isActive: boolean;
}

export interface UpdateProductCategoryResult {
  id: string;
  name: string;
  parentCategoryId: string | null;
  isActive: boolean;
}

export interface UnitOfMeasurement {
  id: string;
  organizationId: string;
  name: string;
  shortName: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateUnitOfMeasurementRequest {
  name: string;
  shortName: string;
}

export interface CreateUnitOfMeasurementResult {
  id: string;
  name: string;
  shortName: string;
}

export interface UpdateUnitOfMeasurementRequest {
  name: string;
  shortName: string;
  isActive: boolean;
}

export interface UpdateUnitOfMeasurementResult {
  id: string;
  name: string;
  shortName: string;
  isActive: boolean;
}

export interface ProductSecondaryUnit {
  id: string;
  productId: string;
  unitId: string;
  conversionRate: number;
  sellingPrice: number;
  purchasePrice: number;
}

export interface Product {
  id: string;
  organizationId: string;
  type: ProductType;
  name: string;
  code: string;
  categoryId: string;
  primaryUnitId: string;
  hsCode: string | null;
  availableForSale: boolean;
  sellingPrice: number;
  purchasePrice: number;
  vatRate: VatRate;
  valuationMethod: 'Fifo';
  reOrderLevel: number;
  trackInventory: boolean;
  isActive: boolean;
  createdAt: string;

  /** FR-8.3. Present on every product -- a variant IS a product (Phase 24, Decision A). */
  sku: string | null;
  barcode: string | null;

  /** Set on a variant child, pointing at its parent. Null on an ordinary product and on a parent. */
  parentProductId: string | null;

  /**
   * True on a variant parent, which is NOT selectable on a document line -- pick one of its
   * variants instead. Pickers never see one: `listAllProducts` filters them out server-side.
   */
  hasVariants: boolean;

  secondaryUnits: ProductSecondaryUnit[];
  salesAccountId: string | null;
  salesReturnAccountId: string | null;
  purchaseAccountId: string | null;
  purchaseReturnAccountId: string | null;
}

export interface CreateProductRequest {
  type: ProductType;
  name: string;
  categoryId: string;
  primaryUnitId: string;
  hsCode: string | null;
  availableForSale: boolean;
  sellingPrice: number;
  purchasePrice: number;
  vatRate: VatRate;
  reOrderLevel: number;
  trackInventory: boolean;
  sku?: string | null;
  barcode?: string | null;
}

export interface CreateProductResult {
  id: string;
  code: string;
  type: ProductType;
  name: string;
}

export interface UpdateProductRequest {
  name: string;
  categoryId: string;
  primaryUnitId: string;
  hsCode: string | null;
  availableForSale: boolean;
  sellingPrice: number;
  purchasePrice: number;
  vatRate: VatRate;
  reOrderLevel: number;
  trackInventory: boolean;
  isActive: boolean;
  salesAccountId?: string | null;
  salesReturnAccountId?: string | null;
  purchaseAccountId?: string | null;
  purchaseReturnAccountId?: string | null;
  sku?: string | null;
  barcode?: string | null;
}

export interface UpdateProductResult {
  id: string;
  name: string;
}

export interface AddSecondaryUnitRequest {
  unitId: string;
  conversionRate: number;
  sellingPrice: number;
  purchasePrice: number;
}

export interface AddSecondaryUnitResult {
  id: string;
  productId: string;
  unitId: string;
  conversionRate: number;
}


/**
 * Phase 24 (FR-8.3). Which of the three roles a Product can play a list should return.
 * Mirrors the server's ProductVariantFilter exactly.
 */
export type ProductVariantFilter = 'All' | 'Transactable' | 'VariantParents';

/** The tenant-global attribute catalog: Size, Color, RAM, ... */
export interface VariantAttribute {
  id: string;
  name: string;
  isActive: boolean;
  options: VariantAttributeOption[];
}

export interface VariantAttributeOption {
  id: string;
  value: string;
  sortOrder: number;
  isActive: boolean;
}

export interface CreateVariantAttributeRequest {
  name: string;
  options: string[];
}

export interface UpdateVariantAttributeRequest {
  name: string;
  isActive: boolean;
}

export interface UpdateVariantAttributeOptionRequest {
  value: string;
  isActive: boolean;
}

/** One (attribute, option) pair on the wire. */
export interface VariantCombinationInput {
  attributeId: string;
  optionId: string;
}

/** A parent's "Attributes Used" pool, grouped one row per attribute. */
export interface ProductVariantAttributeUsage {
  attributeId: string;
  attributeName: string;
  options: { optionId: string; value: string }[];
}

export interface ProductVariantValue {
  attributeId: string;
  attributeName: string;
  optionId: string;
  optionValue: string;
}

/** One variant child -- the live product's Variant Details row. */
export interface ProductVariant {
  id: string;
  parentProductId: string;
  code: string;
  name: string;
  sku: string | null;
  barcode: string | null;
  sellingPrice: number;
  purchasePrice: number;
  isActive: boolean;
  attributeValues: ProductVariantValue[];
}

/** The whole variant panel in one round trip. */
export interface ProductVariantPanel {
  productId: string;
  hasVariants: boolean;
  attributesUsed: ProductVariantAttributeUsage[];
  variants: ProductVariant[];
}

export interface ProductVariantAttributesResult {
  productId: string;
  hasVariants: boolean;
  usages: ProductVariantAttributeUsage[];
}

export interface CreateProductVariantRequest {
  combination: VariantCombinationInput[];
  name: string | null;
  sku: string | null;
  barcode: string | null;
  sellingPrice: number;
  purchasePrice: number;
}

export interface UpdateProductVariantRequest {
  name: string;
  sku: string | null;
  barcode: string | null;
  sellingPrice: number;
  purchasePrice: number;
  isActive: boolean;
}

export interface GenerateProductVariantsResult {
  productId: string;
  skippedExisting: number;
  created: ProductVariant[];
}
