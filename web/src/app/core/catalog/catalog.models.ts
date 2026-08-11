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
  secondaryUnits: ProductSecondaryUnit[];
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
