import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  AddSecondaryUnitRequest,
  AddSecondaryUnitResult,
  CreateProductCategoryRequest,
  CreateProductCategoryResult,
  CreateProductRequest,
  CreateProductResult,
  CreateUnitOfMeasurementRequest,
  CreateUnitOfMeasurementResult,
  Product,
  ProductCategory,
  ProductType,
  UnitOfMeasurement,
  UpdateProductCategoryRequest,
  UpdateProductCategoryResult,
  UpdateProductRequest,
  UpdateProductResult,
  UpdateUnitOfMeasurementRequest,
  UpdateUnitOfMeasurementResult,
} from './catalog.models';

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  listProductCategories(organizationId: string): Observable<ProductCategory[]> {
    return this.http.get<ProductCategory[]>(`${this.baseUrl(organizationId)}/product-categories`, {
      withCredentials: true,
    });
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
    return this.http.get<UnitOfMeasurement[]>(`${this.baseUrl(organizationId)}/units-of-measurement`, {
      withCredentials: true,
    });
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

  listProducts(organizationId: string, type?: ProductType): Observable<Product[]> {
    const params: Record<string, string> = type ? { type } : {};
    return this.http.get<Product[]>(`${this.baseUrl(organizationId)}/products`, { withCredentials: true, params });
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
