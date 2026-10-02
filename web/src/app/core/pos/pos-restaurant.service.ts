import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { PagedResult } from '../common/paged-result';
import { ListQueryOptions, applyListOptions } from '../../shared/pagination/list-query-options';
import { PosProduct } from './pos.models';
import {
  CreatePosOrderRequest,
  KitchenStation,
  PosFloorPlan,
  PosKitchenTicketPrint,
  PosOrder,
  PosOrderItemInput,
  PosOrderLineQuantityInput,
  PosOrderStatus,
  PosOrderType,
  PosRestaurant,
  PosTableLayoutInput,
  UpdatePosOrderRequest,
} from './pos-restaurant.models';

/**
 * Phase 64 -- the restaurant's calls: the floor plan and kitchen stations (configuration), the
 * restaurant till's floor and orders, and the ERP's POS Orders list. All under the till's `/pos` prefix.
 */
@Injectable({ providedIn: 'root' })
export class PosRestaurantService {
  private readonly http = inject(HttpClient);

  // ---- Configuration --------------------------------------------------------------------------

  getFloorPlan(organizationId: string, locationId: string): Observable<PosFloorPlan> {
    return this.http.get<PosFloorPlan>(`${this.baseUrl(organizationId)}/locations/${locationId}/floor-plan`, {
      withCredentials: true,
    });
  }

  createArea(organizationId: string, locationId: string, name: string): Observable<PosFloorPlan> {
    return this.http.post<PosFloorPlan>(`${this.baseUrl(organizationId)}/locations/${locationId}/areas`, { name }, {
      withCredentials: true,
    });
  }

  updateArea(organizationId: string, areaId: string, name: string, isActive: boolean): Observable<PosFloorPlan> {
    return this.http.put<PosFloorPlan>(`${this.baseUrl(organizationId)}/areas/${areaId}`, { name, isActive }, {
      withCredentials: true,
    });
  }

  /** The area's whole layout: every table already on it, plus any new ones (id null). */
  saveLayout(organizationId: string, areaId: string, tables: PosTableLayoutInput[]): Observable<PosFloorPlan> {
    return this.http.put<PosFloorPlan>(`${this.baseUrl(organizationId)}/areas/${areaId}/layout`, { tables }, {
      withCredentials: true,
    });
  }

  listKitchenStations(organizationId: string): Observable<KitchenStation[]> {
    return this.http.get<KitchenStation[]>(`${this.baseUrl(organizationId)}/kitchen-stations`, { withCredentials: true });
  }

  createKitchenStation(organizationId: string, name: string): Observable<KitchenStation[]> {
    return this.http.post<KitchenStation[]>(`${this.baseUrl(organizationId)}/kitchen-stations`, { name }, {
      withCredentials: true,
    });
  }

  updateKitchenStation(
    organizationId: string, stationId: string, name: string, isActive: boolean,
  ): Observable<KitchenStation[]> {
    return this.http.put<KitchenStation[]>(
      `${this.baseUrl(organizationId)}/kitchen-stations/${stationId}`, { name, isActive }, { withCredentials: true });
  }

  /** The whole list of products sent to this station; any left off go back to Default. */
  setKitchenStationProducts(
    organizationId: string, stationId: string, productIds: string[],
  ): Observable<KitchenStation[]> {
    return this.http.put<KitchenStation[]>(
      `${this.baseUrl(organizationId)}/kitchen-stations/${stationId}/products`, { productIds },
      { withCredentials: true });
  }

  // ---- The restaurant till --------------------------------------------------------------------

  getRestaurant(organizationId: string, locationId: string): Observable<PosRestaurant> {
    return this.http.get<PosRestaurant>(`${this.baseUrl(organizationId)}/restaurants/${locationId}`, {
      withCredentials: true,
    });
  }

  listProducts(
    organizationId: string,
    locationId: string,
    filter: { search?: string; categoryId?: string; page?: number; pageSize?: number },
  ): Observable<PagedResult<PosProduct>> {
    const params: Record<string, string> = {};
    if (filter.search) params['search'] = filter.search;
    if (filter.categoryId) params['categoryId'] = filter.categoryId;
    if (filter.page) params['page'] = String(filter.page);
    if (filter.pageSize) params['pageSize'] = String(filter.pageSize);
    return this.http.get<PagedResult<PosProduct>>(`${this.baseUrl(organizationId)}/restaurants/${locationId}/products`, {
      withCredentials: true,
      params,
    });
  }

  createOrder(organizationId: string, request: CreatePosOrderRequest): Observable<PosOrder> {
    return this.http.post<PosOrder>(`${this.baseUrl(organizationId)}/orders`, request, { withCredentials: true });
  }

  getOrder(organizationId: string, orderId: string): Observable<PosOrder> {
    return this.http.get<PosOrder>(`${this.orderUrl(organizationId, orderId)}`, { withCredentials: true });
  }

  updateOrder(organizationId: string, orderId: string, request: UpdatePosOrderRequest): Observable<PosOrder> {
    return this.http.put<PosOrder>(this.orderUrl(organizationId, orderId), request, { withCredentials: true });
  }

  /** A send: new items and more of lines already on the order, ticketed per kitchen station. */
  addItems(
    organizationId: string, orderId: string, newItems: PosOrderItemInput[], moreOf: PosOrderLineQuantityInput[],
  ): Observable<PosOrder> {
    return this.http.post<PosOrder>(`${this.orderUrl(organizationId, orderId)}/items`, { newItems, moreOf }, {
      withCredentials: true,
    });
  }

  serve(organizationId: string, orderId: string, items: PosOrderLineQuantityInput[]): Observable<PosOrder> {
    return this.http.post<PosOrder>(`${this.orderUrl(organizationId, orderId)}/serve`, { items }, {
      withCredentials: true,
    });
  }

  discard(
    organizationId: string, orderId: string, items: PosOrderLineQuantityInput[], reason: string,
  ): Observable<PosOrder> {
    return this.http.post<PosOrder>(`${this.orderUrl(organizationId, orderId)}/discard`, { items, reason }, {
      withCredentials: true,
    });
  }

  voidOrder(organizationId: string, orderId: string, reason: string): Observable<PosOrder> {
    return this.http.post<PosOrder>(`${this.orderUrl(organizationId, orderId)}/void`, { reason }, {
      withCredentials: true,
    });
  }

  /** Every call is a printing the server counts: 1 is the original, anything later a reprint. */
  printTicket(organizationId: string, orderId: string, ticketId: string): Observable<PosKitchenTicketPrint> {
    return this.http.post<PosKitchenTicketPrint>(
      `${this.orderUrl(organizationId, orderId)}/tickets/${ticketId}/prints`, {}, { withCredentials: true });
  }

  // ---- The ERP's POS Orders list --------------------------------------------------------------

  listOrders(
    organizationId: string,
    filter: { status?: PosOrderStatus; orderType?: PosOrderType; page: number; pageSize: number },
    options?: ListQueryOptions,
  ): Observable<PagedResult<PosOrder>> {
    const params: Record<string, string> = { page: String(filter.page), pageSize: String(filter.pageSize) };
    applyListOptions(params, options);
    if (filter.status) params['status'] = filter.status;
    if (filter.orderType) params['orderType'] = filter.orderType;
    return this.http.get<PagedResult<PosOrder>>(`${this.baseUrl(organizationId)}/orders`, {
      withCredentials: true,
      params,
    });
  }

  private orderUrl(organizationId: string, orderId: string): string {
    return `${this.baseUrl(organizationId)}/orders/${orderId}`;
  }

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}/pos`;
  }
}
