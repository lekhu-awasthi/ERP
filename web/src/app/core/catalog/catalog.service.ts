import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MAX_PAGE_SIZE, PagedResult } from '../common/paged-result';
import {
  AddSecondaryUnitRequest,
  AddSecondaryUnitResult,
  CreateProductCategoryRequest,
  CreateProductCategoryResult,
  CreateProductRequest,
  CreateProductResult,
  CreateProductVariantRequest,
  CreateUnitOfMeasurementRequest,
  CreateUnitOfMeasurementResult,
  CreateVariantAttributeRequest,
  GenerateProductVariantsResult,
  Product,
  ProductCategory,
  ProductType,
  ProductVariant,
  ProductVariantAttributesResult,
  ProductVariantFilter,
  ProductVariantPanel,
  UnitOfMeasurement,
  UpdateProductCategoryRequest,
  UpdateProductCategoryResult,
  UpdateProductRequest,
  UpdateProductResult,
  UpdateProductVariantRequest,
  UpdateUnitOfMeasurementRequest,
  UpdateUnitOfMeasurementResult,
  UpdateVariantAttributeOptionRequest,
  UpdateVariantAttributeRequest,
  VariantAttribute,
  VariantCombinationInput,
} from './catalog.models';

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  /** Bounded master-data / picker lists (Phase 16c) -- no visible pager, just request everything
   * in one page and unwrap, keeping every caller's Observable<T[]> contract intact. */
  private listAll<T>(url: string, extraParams: Record<string, string> = {}): Observable<T[]> {
    return this.http
      .get<PagedResult<T>>(url, {
        withCredentials: true,
        params: { ...extraParams, page: '1', pageSize: String(MAX_PAGE_SIZE) },
      })
      .pipe(map((result) => result.items));
  }

  listProductCategories(organizationId: string): Observable<ProductCategory[]> {
    return this.listAll<ProductCategory>(`${this.baseUrl(organizationId)}/product-categories`);
  }

  createProductCategory(
    organizationId: string,
    request: CreateProductCategoryRequest,
  ): Observable<CreateProductCategoryResult> {
    return this.http.post<CreateProductCategoryResult>(`${this.baseUrl(organizationId)}/product-categories`, request, {
      withCredentials: true,
    });
  }

  updateProductCategory(
    organizationId: string,
    id: string,
    request: UpdateProductCategoryRequest,
  ): Observable<UpdateProductCategoryResult> {
    return this.http.put<UpdateProductCategoryResult>(
      `${this.baseUrl(organizationId)}/product-categories/${id}`,
      request,
      { withCredentials: true },
    );
  }

  deleteProductCategory(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/product-categories/${id}`, {
      withCredentials: true,
    });
  }

  listUnitsOfMeasurement(organizationId: string): Observable<UnitOfMeasurement[]> {
    return this.listAll<UnitOfMeasurement>(`${this.baseUrl(organizationId)}/units-of-measurement`);
  }

  createUnitOfMeasurement(
    organizationId: string,
    request: CreateUnitOfMeasurementRequest,
  ): Observable<CreateUnitOfMeasurementResult> {
    return this.http.post<CreateUnitOfMeasurementResult>(
      `${this.baseUrl(organizationId)}/units-of-measurement`,
      request,
      { withCredentials: true },
    );
  }

  updateUnitOfMeasurement(
    organizationId: string,
    id: string,
    request: UpdateUnitOfMeasurementRequest,
  ): Observable<UpdateUnitOfMeasurementResult> {
    return this.http.put<UpdateUnitOfMeasurementResult>(
      `${this.baseUrl(organizationId)}/units-of-measurement/${id}`,
      request,
      { withCredentials: true },
    );
  }

  deleteUnitOfMeasurement(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/units-of-measurement/${id}`, {
      withCredentials: true,
    });
  }

  listProducts(
    organizationId: string,
    type?: ProductType,
    page = 1,
    pageSize = 50,
    variantFilter: ProductVariantFilter = 'All',
  ): Observable<PagedResult<Product>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (type) params['type'] = type;
    if (variantFilter !== 'All') params['variantFilter'] = variantFilter;
    return this.http.get<PagedResult<Product>>(`${this.baseUrl(organizationId)}/products`, { withCredentials: true, params });
  }

  /**
   * Picker use (e.g. an invoice line's Product dropdown) -- everything in one page, no pager.
   *
   * **Phase 24's entire client-side sweep lives on this one line.** Every one of the fifteen
   * product pickers and report filters in this app calls this method (asserted by
   * `catalog.service.spec.ts`'s guard), so defaulting it to `Transactable` makes all of them
   * variant-aware at once: variant children appear as ordinary selectable entries -- which is
   * exactly how the live reference product presents them, confirmed in the browser -- and variant
   * *parents* disappear, because a parent is not transactable (see ProductVariantRules server-side).
   *
   * Report filter pages want the same list for a different reason: a parent has no stock and no
   * transactions, so offering one would only ever produce an empty report.
   */
  listAllProducts(
    organizationId: string,
    type?: ProductType,
    variantFilter: ProductVariantFilter = 'Transactable',
  ): Observable<Product[]> {
    return this.listProducts(organizationId, type, 1, MAX_PAGE_SIZE, variantFilter).pipe(map((result) => result.items));
  }

  // ---- Phase 24: the tenant-global attribute catalog ----

  listVariantAttributes(organizationId: string, activeOnly = false): Observable<PagedResult<VariantAttribute>> {
    const params: Record<string, string> = { pageSize: String(MAX_PAGE_SIZE) };
    if (activeOnly) params['activeOnly'] = 'true';
    return this.http.get<PagedResult<VariantAttribute>>(`${this.baseUrl(organizationId)}/variant-attributes`, {
      withCredentials: true,
      params,
    });
  }

  createVariantAttribute(organizationId: string, request: CreateVariantAttributeRequest): Observable<VariantAttribute> {
    return this.http.post<VariantAttribute>(`${this.baseUrl(organizationId)}/variant-attributes`, request, {
      withCredentials: true,
    });
  }

  updateVariantAttribute(
    organizationId: string,
    id: string,
    request: UpdateVariantAttributeRequest,
  ): Observable<VariantAttribute> {
    return this.http.put<VariantAttribute>(`${this.baseUrl(organizationId)}/variant-attributes/${id}`, request, {
      withCredentials: true,
    });
  }

  addVariantAttributeOption(organizationId: string, id: string, value: string): Observable<VariantAttribute> {
    return this.http.post<VariantAttribute>(
      `${this.baseUrl(organizationId)}/variant-attributes/${id}/options`,
      { value },
      { withCredentials: true },
    );
  }

  updateVariantAttributeOption(
    organizationId: string,
    id: string,
    optionId: string,
    request: UpdateVariantAttributeOptionRequest,
  ): Observable<VariantAttribute> {
    return this.http.put<VariantAttribute>(
      `${this.baseUrl(organizationId)}/variant-attributes/${id}/options/${optionId}`,
      request,
      { withCredentials: true },
    );
  }

  // ---- Phase 24: a product's own variants ----

  getProductVariants(organizationId: string, productId: string): Observable<ProductVariantPanel> {
    return this.http.get<ProductVariantPanel>(`${this.baseUrl(organizationId)}/products/${productId}/variants`, {
      withCredentials: true,
    });
  }

  setProductVariantAttributes(
    organizationId: string,
    productId: string,
    usages: VariantCombinationInput[],
  ): Observable<ProductVariantAttributesResult> {
    return this.http.put<ProductVariantAttributesResult>(
      `${this.baseUrl(organizationId)}/products/${productId}/variant-attributes`,
      { usages },
      { withCredentials: true },
    );
  }

  createProductVariant(
    organizationId: string,
    productId: string,
    request: CreateProductVariantRequest,
  ): Observable<ProductVariant> {
    return this.http.post<ProductVariant>(`${this.baseUrl(organizationId)}/products/${productId}/variants`, request, {
      withCredentials: true,
    });
  }

  generateProductVariants(organizationId: string, productId: string): Observable<GenerateProductVariantsResult> {
    return this.http.post<GenerateProductVariantsResult>(
      `${this.baseUrl(organizationId)}/products/${productId}/variants/generate`,
      {},
      { withCredentials: true },
    );
  }

  updateProductVariant(
    organizationId: string,
    productId: string,
    variantId: string,
    request: UpdateProductVariantRequest,
  ): Observable<ProductVariant> {
    return this.http.put<ProductVariant>(
      `${this.baseUrl(organizationId)}/products/${productId}/variants/${variantId}`,
      request,
      { withCredentials: true },
    );
  }

  deleteProductVariant(organizationId: string, productId: string, variantId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl(organizationId)}/products/${productId}/variants/${variantId}`,
      { withCredentials: true },
    );
  }

  getProduct(organizationId: string, id: string): Observable<Product> {
    return this.http.get<Product>(`${this.baseUrl(organizationId)}/products/${id}`, { withCredentials: true });
  }

  createProduct(organizationId: string, request: CreateProductRequest): Observable<CreateProductResult> {
    return this.http.post<CreateProductResult>(`${this.baseUrl(organizationId)}/products`, request, {
      withCredentials: true,
    });
  }

  updateProduct(organizationId: string, id: string, request: UpdateProductRequest): Observable<UpdateProductResult> {
    return this.http.put<UpdateProductResult>(`${this.baseUrl(organizationId)}/products/${id}`, request, {
      withCredentials: true,
    });
  }

  addSecondaryUnit(
    organizationId: string,
    productId: string,
    request: AddSecondaryUnitRequest,
  ): Observable<AddSecondaryUnitResult> {
    return this.http.post<AddSecondaryUnitResult>(
      `${this.baseUrl(organizationId)}/products/${productId}/secondary-units`,
      request,
      { withCredentials: true },
    );
  }
}
